"""
Makes an Arabic help topic from its English file by replacing each English passage with
its Arabic, for topics that are mostly markup (diagrams, icon sets) where retyping the
markup would be error-prone.

    python Tools/Localization/translate_topic.py workflow Tools/Localization/help/workflow.txt

The sheet holds "English ||| Arabic" lines, applied in order; every English passage must be
found, or nothing is written. The result is wwwroot/help/topics/ar/<key>.html.
"""
import os, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

def main():
    key, sheet = sys.argv[1], sys.argv[2]
    source = open(os.path.join(PROJECT, 'wwwroot', 'help', 'topics', key + '.html'), encoding='utf-8-sig').read()
    missing = []
    for line in open(sheet, encoding='utf-8').read().splitlines():
        if not line.strip() or line.startswith('#') or '|||' not in line:
            continue
        english, arabic = (x.strip() for x in line.split('|||', 1))
        if english not in source:
            missing.append(english)
            continue
        source = source.replace(english, arabic)
    if missing:
        print('Not found:'); [print('  ', m) for m in missing]; sys.exit(1)
    out = os.path.join(PROJECT, 'wwwroot', 'help', 'topics', 'ar', key + '.html')
    open(out, 'w', encoding='utf-8').write(source)
    print('written', os.path.relpath(out, PROJECT))

if __name__ == '__main__':
    main()
