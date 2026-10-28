"""
Checks the Arabic help topics against the English ones:
  - which topics have no Arabic file yet;
  - English words left in an Arabic file's text (code, account codes and product names aside);
  - screenshots, videos and topic links that the English has and the Arabic lost.

    python Tools/Localization/check_topics.py [key...]
"""
import glob, os, re, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TOPICS = os.path.join(PROJECT, 'wwwroot', 'help', 'topics')
# Words that stay in Latin letters in the Arabic text.
KEEP = {'RealEstateManager', 'PDC', 'PDCs', 'VAT', 'TRN', 'TIN', 'IBAN', 'WPS', 'MOHRE', 'FTA', 'IAS', 'IFRS', 'UAE', 'AED',
        'PDF', 'CSV', 'XML', 'JPG', 'PNG', 'HTTPS', 'HSTS', 'UBL', 'PINT', 'SAP', 'Odoo', 'Excel', 'Ejari', 'Makani',
        'Dr', 'Cr', 'QR', 'ID', 'UUID', 'ADMIN', 'OpenAI', 'Chrome', 'Edge', 'Safari', 'Android', 'iPhone', 'Enter', 'Backspace'}

def text_of(html):
    html = re.sub(r'<svg\b.*?</svg>|<!--.*?-->|<code>.*?</code>|<kbd>.*?</kbd>|<script\b.*?</script>|<style\b.*?</style>', ' ', html, flags=re.S)
    return re.sub(r'<[^>]+>', ' ', html)

def refs(html):
    return sorted(re.findall(r'data-(?:shot|video)="[^"]+"|href="#[^"]+"', html))

def main():
    keys = sys.argv[1:] or sorted(os.path.basename(p)[:-5] for p in glob.glob(os.path.join(TOPICS, '*.html')))
    todo, report = [], []
    for key in keys:
        en_path = os.path.join(TOPICS, key + '.html')
        ar_path = os.path.join(TOPICS, 'ar', key + '.html')
        if not os.path.exists(ar_path):
            todo.append(key); continue
        en = open(en_path, encoding='utf-8-sig').read()
        ar = open(ar_path, encoding='utf-8-sig').read()
        words = [w for w in re.findall(r'[A-Za-z][A-Za-z\'-]{2,}', text_of(ar)) if w not in KEEP]
        lost = sorted(set(refs(en)) - set(refs(ar)))
        if words or lost:
            report.append(f'{key}: {len(words)} English word(s) {" ".join(words[:12])}' + (f' | lost: {" ".join(lost)}' if lost else ''))
    print(f'{len(keys) - len(todo)} of {len(keys)} topics translated')
    if todo:
        print('still to translate:', ' '.join(todo))
    for line in report:
        print(' ', line)

if __name__ == '__main__':
    main()
