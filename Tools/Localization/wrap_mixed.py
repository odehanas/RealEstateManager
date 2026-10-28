"""
Second pass over the Razor views: sentences built around values.

    <div>Occupied: @Model.Occupied</div>
        ->  <div>@T.Format("Occupied: {0}", Model.Occupied)</div>
    <text>Access for <strong>@company</strong> has been suspended.</text>
        ->  <text>@T.HtmlFormat("Access for <strong>{0}</strong> has been suspended.", company)</text>

and English words chosen in a condition:   @(x ? "Yes" : "No")  ->  @(x ? T["Yes"] : T["No"])

Only an element whose content is text, plain inline tags and Razor expressions is
converted; one with code in it (@if, braces, semicolons) is left alone.

    python Tools/Localization/wrap_mixed.py [--dry-run] [files...]

New phrases are added to Resources/Translations/phrases.txt.
"""
import glob, html, os, re, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wrap_views import SKIP, MASK, cs, words, has_letters, in_string

BLOCK = r'h[1-6]|p|li|td|th|label|button|a|span|option|small|strong|div|summary|dt|dd|legend|caption|em|b|figcaption|text'
INLINE = r'(?:<(?:strong|em|b|i|u|code|kbd|sup|sub|br)\s*/?>|</(?:strong|em|b|i|u|code|kbd|sup|sub)>)'
OPEN = re.compile(rf'<({BLOCK})(\s[^<>]*?)?>', re.S)
KEYWORDS = ('if', 'else', 'foreach', 'for', 'while', 'switch', 'using', 'section', 'model', 'inject', 'functions', 'await', 'Html', 'RenderBody', 'RenderSection', 'T')

def razor_expr(s, i):
    """The Razor expression starting at s[i] == '@': returns (code, end) or None."""
    j = i + 1
    if j >= len(s):
        return None
    if s[j] == '(':
        end = balanced(s, j, '(', ')')
        return (s[j + 1:end - 1], end) if end else None
    m = re.match(r'[A-Za-z_]\w*', s[j:])
    if not m:
        return None
    word = m.group(0)
    if word in KEYWORDS:
        return None
    k = j + len(word)
    while k < len(s):
        if s[k] == '(' or s[k] == '[':
            end = balanced(s, k, s[k], ')' if s[k] == '(' else ']')
            if not end:
                return None
            k = end
        elif s[k] == '.' and k + 1 < len(s) and re.match(r'[A-Za-z_]', s[k + 1]):
            m = re.match(r'[A-Za-z_]\w*', s[k + 1:]); k += 1 + len(m.group(0))
        elif s[k] == '?' and k + 1 < len(s) and s[k + 1] == '.':
            k += 1
        elif s[k] == '!' and k + 1 < len(s) and s[k + 1] in '.[':
            k += 1
        else:
            break
    return s[j:k], k

def balanced(s, i, open_, close):
    depth, k, quote = 0, i, None
    while k < len(s):
        c = s[k]
        if quote:
            if c == '\\': k += 2; continue
            if c == quote: quote = None
        elif c in '"\'':
            quote = c
        elif c == open_:
            depth += 1
        elif c == close:
            depth -= 1
            if depth == 0:
                return k + 1
        k += 1
    return None

TERNARY = re.compile(r'([?:]\s*)"([A-Z][A-Za-z ,.\'()/&%-]*[a-z.)][A-Za-z ,.\'()/&%-]*)"(?=\s*(?:[:)]|$))')

def ternaries(code, phrases):
    """English words chosen in a condition become T[...]."""
    def sub(m):
        text = m.group(2)
        if not re.search(r'[a-z]{2,}', text) or re.fullmatch(r'[A-Z][a-z]+(?:[A-Z][a-z]+)+', text):
            return m.group(0)          # CamelCase values are data, not words
        phrases.add(words(text))
        return f'{m.group(1)}T[{cs(words(text))}]'
    return TERNARY.sub(sub, code)

def parse_inner(inner):
    """Splits an element's content into a template and its values, or None if it holds code."""
    parts, args, k = [], [], 0
    while k < len(inner):
        c = inner[k]
        if c == '@':
            if inner[k:k + 2] == '@@':
                parts.append('@'); k += 2; continue
            if k > 0 and re.match(r'\w', inner[k - 1]):
                return None            # an e-mail address or similar
            r = razor_expr(inner, k)
            if not r:
                return None
            code, end = r
            if code.startswith(('T[', 'T.')) and not code.startswith('T.Format'):
                pass
            parts.append('{%d}' % len(args)); args.append(code); k = end; continue
        if c == '<':
            m = re.match(INLINE, inner[k:])
            if not m:
                return None
            parts.append(m.group(0)); k += len(m.group(0)); continue
        if c in '{};':
            if c in '{}':
                return None
        parts.append(c); k += 1
    return ''.join(parts), args

def tag_end(s, i):
    """End of the opening tag at s[i] == '<', skipping Razor expressions and quoted values."""
    k, quote = i + 1, None
    while k < len(s):
        if s.startswith('@(', k):
            e = balanced(s, k + 1, '(', ')')
            if not e:
                return None
            k = e; continue
        c = s[k]
        if quote:
            if c == quote: quote = None
        elif c in '"\'':
            quote = c
        elif c == '<':
            return None
        elif c == '>':
            return k + 1
        k += 1
    return None

START = re.compile(rf'<({BLOCK})(?=[\s>])')

def convert(source, phrases):
    masked = MASK.sub(lambda m: '\0' * len(m.group(0)), source)
    out, last, count, pos = [], 0, 0, 0
    while True:
        m = START.search(masked, pos)
        if not m:
            break
        tag = m.group(1)
        open_end = tag_end(masked, m.start())
        if not open_end:
            pos = m.end(); continue
        close = masked.find(f'</{tag}>', open_end)
        nxt = re.compile(rf'<{tag}[\s>]').search(masked, open_end)
        if close < 0 or (nxt and nxt.start() < close) or in_string(masked, m.start()):
            pos = open_end; continue
        inner = masked[open_end:close]
        if '\0' in inner or '@' not in inner:
            pos = open_end; continue
        parsed = parse_inner(inner)
        if not parsed or not parsed[1]:
            pos = open_end; continue
        template, args = parsed
        literal = re.sub(r'\{\d+\}|<[^>]+>', ' ', template)
        if not has_letters(html.unescape(literal)) or not re.search(r'[A-Za-z]{3,}', literal):
            pos = open_end; continue
        lead = template[:len(template) - len(template.lstrip())]
        trail = template[len(template.rstrip()):]
        body = template.strip()
        args = [ternaries(a, phrases) for a in args]
        if '<' in body:
            key = words(body).replace('{{', '{').replace('}}', '}')
            call = f'@T.HtmlFormat({cs(key)}, {", ".join(args)})'
        else:
            key = words(html.unescape(body))
            call = f'@T.Format({cs(key)}, {", ".join(args)})'
        phrases.add(key)
        out.append(source[last:open_end] + lead + call + trail)
        last = close
        count += 1
        pos = close
    out.append(source[last:])
    result = ''.join(out)

    # English words chosen in a condition elsewhere: @(x ? "Yes" : "No") in text and attributes.
    def explicit(mm):
        return '@(' + ternaries(mm.group(1), phrases) + ')'
    result2, k, pieces = result, 0, []
    i = 0
    while True:
        j = result2.find('@(', i)
        if j < 0:
            pieces.append(result2[i:]); break
        end = balanced(result2, j + 1, '(', ')')
        if not end:
            pieces.append(result2[i:]); break
        code = result2[j + 2:end - 1]
        new = ternaries(code, phrases)
        if new != code: count += 1
        pieces.append(result2[i:j] + '@(' + new + ')')
        i = end
    return ''.join(pieces), count

def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    dry = '--dry-run' in sys.argv
    files = sorted({os.path.relpath(f, PROJECT).replace('\\', '/')
                    for p in (args or ['Views/**/*.cshtml'])
                    for f in glob.glob(os.path.join(PROJECT, p), recursive=True) if f.endswith('.cshtml')})
    phrases, total = set(), 0
    for rel in files:
        if rel in SKIP:
            continue
        path = os.path.join(PROJECT, rel)
        raw = open(path, 'rb').read()
        bom, crlf = raw.startswith(b'\xef\xbb\xbf'), b'\r\n' in raw
        source = raw.decode('utf-8-sig').replace('\r\n', '\n')
        result, count = convert(source, phrases)
        total += count
        if not dry and count:
            open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + (result.replace('\n', '\r\n') if crlf else result).encode('utf-8'))
    print(f'{total} sentences converted; {len(phrases)} distinct phrases')
    if not dry:
        out = os.path.join(PROJECT, 'Resources', 'Translations', 'phrases.txt')
        existing = set(open(out, encoding='utf-8').read().splitlines())
        open(out, 'w', encoding='utf-8').write('\n'.join(sorted((existing | phrases) - {''})) + '\n')

if __name__ == '__main__':
    main()
