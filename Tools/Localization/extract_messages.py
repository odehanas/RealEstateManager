"""
Collects the messages the code shows to users - errors, confirmations, validation and
the reasons a step is refused - as templates for the Arabic dictionary:

    $"Cheque {pdc.ChequeNumber} is deposited to the bank."  ->  Cheque {0} is deposited to the bank.

Shown messages are translated as they are drawn (UiMessageTranslationFilter), by
matching them against these templates, so no code has to change.

    python Tools/Localization/extract_messages.py   # writes Resources/Translations/messages.txt
"""
import glob, os, re

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# The assistant's prompts and tool descriptions are for the AI model, not the user.
SKIP_DIRS = ('Services/Assistant', 'Services/SampleDataService.cs', 'Services/ChartOfAccountsSeeder.cs', 'Migrations', 'Tools', 'bin', 'obj')

LITERAL = re.compile(r'(\$@|@\$|\$|@)?"((?:[^"\\\n]|\\.|"")*)"')

def interpolation_to_template(body, interpolated):
    if not interpolated:
        return body
    out, depth, n, k = [], 0, 0, 0
    while k < len(body):
        c = body[k]
        if c == '{' and body[k:k + 2] == '{{':
            out.append('{'); k += 2; continue
        if c == '}' and body[k:k + 2] == '}}':
            out.append('}'); k += 2; continue
        if c == '{':
            depth, j = 1, k + 1
            while j < len(body) and depth:
                if body[j] == '{': depth += 1
                elif body[j] == '}': depth -= 1
                elif body[j] == '"':
                    j = body.find('"', j + 1)
                    if j < 0: return None
                j += 1
            out.append('{%d}' % n); n += 1; k = j; continue
        out.append(c); k += 1
    return ''.join(out)

def joined_literals(source):
    """String literals, with "a" + "b" concatenations across lines joined into one."""
    items = []
    for m in LITERAL.finditer(source):
        prefix, body = m.group(1) or '', m.group(2)
        items.append([m.start(), m.end(), prefix, body])
    i = 0
    while i < len(items):
        start, end, prefix, body = items[i]
        parts = [(prefix, body)]
        while i + 1 < len(items) and re.fullmatch(r'\s*\+\s*', source[end:items[i + 1][0]]):
            i += 1
            end = items[i][1]
            parts.append((items[i][2], items[i][3]))
        text, n = '', 0
        ok = True
        for p, b in parts:
            t = interpolation_to_template(b.replace('\\"', '"').replace('\\n', ' '), '$' in p)
            if t is None: ok = False; break
            # renumber placeholders across joined parts
            def bump(mm):
                return '{%d}' % (int(mm.group(1)) + n)
            text += re.sub(r'\{(\d+)\}', bump, t)
            n += len(re.findall(r'\{\d+\}', t))
        if ok:
            yield start, text
        i += 1

def looks_like_message(text, context):
    t = text.strip()
    if len(t) < 12 or ' ' not in t or not re.match(r'[A-Z{]', t):
        return False
    if re.search(r'</?[a-z]+[ >]|SELECT |INSERT |UPDATE |\bnameof\b|=>|::|\\\\|https?:', t):
        return False
    words = re.findall(r'[A-Za-z]{2,}', re.sub(r'\{\d+\}', '', t))
    if len(words) < 3:
        return False
    # logging is for developers
    return not re.search(r'_logger\.|Log(Information|Warning|Error|Debug)\(|Console\.', context)

def main():
    messages = set()
    for path in glob.glob(os.path.join(PROJECT, '**', '*.cs'), recursive=True):
        rel = os.path.relpath(path, PROJECT).replace('\\', '/')
        if rel.startswith(SKIP_DIRS) or '/obj/' in rel or '/bin/' in rel:
            continue
        source = open(path, encoding='utf-8-sig').read()
        for start, text in joined_literals(source):
            line_start = source.rfind('\n', 0, start) + 1
            context = source[line_start:start]
            if looks_like_message(text, context):
                messages.add(' '.join(text.split()))
    out = os.path.join(PROJECT, 'Resources', 'Translations', 'messages.txt')
    open(out, 'w', encoding='utf-8').write('\n'.join(sorted(messages)) + '\n')
    print(len(messages), 'messages ->', os.path.relpath(out, PROJECT), sum(len(m.split()) for m in messages), 'words')

if __name__ == '__main__':
    main()
