"""
Wraps the English text of the Razor views in the translation helper, so each phrase can
be shown in Arabic:

    <h2>Receive PDC</h2>                  ->  <h2>@T["Receive PDC"]</h2>
    <p>Use <strong>Advance</strong> ...</p> ->  <p>@T.Html("Use <strong>Advance</strong> ...")</p>
    placeholder="Search"                  ->  placeholder="@T["Search"]"
    ViewData["Title"] = "Receive PDC";    ->  ViewData["Title"] = T["Receive PDC"];

Only text with no Razor in it is touched; sentences built from values are left for hand
work (T.Format). Printed documents with a bilingual layout of their own are skipped.

    python Tools/Localization/wrap_views.py --dry-run     # count, change nothing
    python Tools/Localization/wrap_views.py               # wrap, and list the phrases
    python Tools/Localization/wrap_views.py Views/Pdc*    # only some folders/files

The phrases found are written to Resources/Translations/phrases.txt (one per line), the
list the Arabic dictionary (Resources/Translations/ar.json) must cover.
"""
import glob, html, json, os, re, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Documents printed with their own bilingual layout, the help/manual pages, and the
# switch itself.
SKIP = {
    'Views/CustomerInvoices/TaxInvoice.cshtml', 'Views/CustomerCreditNotes/Document.cshtml',
    'Views/OwnerRecharges/Document.cshtml', 'Views/Leases/ContractDraft.cshtml', 'Views/Leases/ContractLabel.cshtml',
    'Views/Payroll/Payslips.cshtml', 'Views/Payroll/BankLetter.cshtml', 'Views/Payroll/Cheques.cshtml',
    'Views/EmployeeRequests/Certificate.cshtml', 'Views/Help/Manual.cshtml', 'Views/Help/Training.cshtml',
    'Views/Shared/_LanguageSwitch.cshtml', 'Views/_ViewImports.cshtml', 'Views/_ViewStart.cshtml',
}

BLOCK = r'h[1-6]|p|li|td|th|label|button|a|span|option|small|strong|div|summary|dt|dd|legend|caption|em|b|figcaption|optgroup'
# Markup allowed inside a sentence: plain inline tags with no Razor and no tag helpers.
INLINE_OPEN = r'<(?:strong|em|b|i|u|code|kbd|sup|sub|br)\s*/?>'
INLINE_CLOSE = r'</(?:strong|em|b|i|u|code|kbd|sup|sub)>'
INNER = rf'(?:[^<@]|{INLINE_OPEN}|{INLINE_CLOSE})*?'
ELEMENT = re.compile(rf'<({BLOCK})(\s[^<>@]*?|\s[^<>]*?)?>({INNER})</\1>', re.S)
ATTRIBUTE = re.compile(r'\b(placeholder|title|aria-label|data-confirm)="([^"@{}<>]*[A-Za-z][^"@{}<>]*)"')
TITLE = re.compile(r'(ViewData\["Title"\]\s*=\s*)"([^"\\]*[A-Za-z][^"\\]*)"\s*;')
MASK = re.compile(r'<script\b.*?</script>|<style\b.*?</style>|@\*.*?\*@', re.S | re.I)

def words(s):
    return ' '.join(s.split())

def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'

def has_letters(text):
    return re.search(r'[A-Za-z]{2,}', text) is not None

def in_string(source, pos):
    # A tag inside a C# or JS string literal on the same line is not markup.
    line = source[source.rfind('\n', 0, pos) + 1:pos]
    stripped = re.sub(r'="[^"]*"', '', line)      # attribute values are not strings
    return stripped.count('"') % 2 == 1 or stripped.count("'") % 2 == 1 or stripped.rstrip().endswith(('+', '='))

INLINE_TAGS = {'strong', 'em', 'b', 'span', 'small', 'a', 'code'}

def mid_sentence(source, start, end):
    # A bold word or link inside a sentence is translated with the sentence, never alone.
    line_start = source.rfind('\n', 0, start) + 1
    line_end = source.find('\n', end)
    before = source[line_start:start]
    after = source[end:line_end if line_end >= 0 else len(source)]
    return bool(re.search(r'[\w,.;:)\]]\s*$', before)) or bool(re.match(r'\s*[\w(,.;:@&]', after))

def convert(source, phrases):
    masked = MASK.sub(lambda m: '\0' * len(m.group(0)), source)
    out, last, count = [], 0, 0

    pos = 0
    while True:
        m = ELEMENT.search(masked, pos)
        if not m:
            break
        tag, attrs, inner = m.group(1), m.group(2) or '', m.group(3)
        start_inner, end_inner = m.start(3), m.end(3)
        text = re.sub(r'<[^>]+>', ' ', inner)
        if ('\0' in inner or not has_letters(html.unescape(text)) or in_string(masked, m.start()) or 'asp-' in inner
                or (tag in INLINE_TAGS and mid_sentence(masked, m.start(), m.end()))):
            # Try again inside this element, past its opening tag.
            pos = m.start() + 1
            continue
        lead = inner[:len(inner) - len(inner.lstrip())]
        trail = inner[len(inner.rstrip()):]
        body = inner.strip()
        if '<' in body:
            key = words(body)
            call = f'@T.Html({cs(key)})'
        else:
            key = words(html.unescape(body))
            call = f'@T[{cs(key)}]'
        phrases.add(key)
        out.append(source[last:start_inner] + lead + call + trail)
        last = end_inner
        count += 1
        pos = m.end()
    out.append(source[last:])
    result = ''.join(out)

    def attr(m):
        nonlocal count
        value = html.unescape(m.group(2))
        phrases.add(words(value)); count += 1
        return f'{m.group(1)}="@T[{cs(words(value))}]"'
    masked2 = MASK.sub(lambda m: '\0' * len(m.group(0)), result)
    pieces, last = [], 0
    for m in ATTRIBUTE.finditer(masked2):
        pieces.append(result[last:m.start()] + attr(m)); last = m.end()
    pieces.append(result[last:])
    result = ''.join(pieces)

    def title(m):
        nonlocal count
        phrases.add(words(m.group(2))); count += 1
        return f'{m.group(1)}T[{cs(words(m.group(2)))}];'
    result = TITLE.sub(title, result)
    return result, count

def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    dry = '--dry-run' in sys.argv
    patterns = args or ['Views/**/*.cshtml']
    files = sorted({os.path.relpath(f, PROJECT).replace('\\', '/')
                    for p in patterns for f in glob.glob(os.path.join(PROJECT, p), recursive=True)
                    if f.endswith('.cshtml')})
    phrases, total = set(), 0
    for rel in files:
        if rel in SKIP:
            continue
        path = os.path.join(PROJECT, rel)
        raw = open(path, 'rb').read()
        bom, crlf = raw.startswith(b'\xef\xbb\xbf'), b'\r\n' in raw
        source = raw.decode('utf-8-sig').replace('\r\n', '\n')
        if '@T[' in source or '@T.Html(' in source:
            # Already wrapped: collect its phrases only.
            for m in re.finditer(r'T\[("(?:[^"\\]|\\.)*")\]|T\.Html\(("(?:[^"\\]|\\.)*")\)|T\.Format\(("(?:[^"\\]|\\.)*")', source):
                phrases.add(json.loads(next(g for g in m.groups() if g)))
            continue
        result, count = convert(source, phrases)
        total += count
        if not dry and count:
            open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + (result.replace('\n', '\r\n') if crlf else result).encode('utf-8'))
    print(f'{total} phrases wrapped in {len(files)} files; {len(phrases)} distinct phrases')
    if not dry:
        out = os.path.join(PROJECT, 'Resources', 'Translations', 'phrases.txt')
        existing = set(open(out, encoding='utf-8').read().splitlines()) if os.path.exists(out) else set()
        open(out, 'w', encoding='utf-8').write('\n'.join(sorted(existing | phrases)) + '\n')

if __name__ == '__main__':
    main()
