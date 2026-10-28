"""
Builds the Arabic dictionary, Resources/Translations/ar.json, from the translation
sheets in Tools/Localization/ar/*.txt.

Each sheet line is   <English, or the start of it> ||| <Arabic>
An English text that is a phrase in Resources/Translations/phrases.txt is used as is;
otherwise it must be the start of exactly one phrase. In a sheet whose name contains
"exact" (field names, messages from the code) the English is always the whole key.
Lines starting with # are notes.

    python Tools/Localization/build_dictionary.py           # build, list problems
    python Tools/Localization/build_dictionary.py --todo    # also list untranslated phrases
"""
import glob, json, os, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PHRASES = os.path.join(PROJECT, 'Resources', 'Translations', 'phrases.txt')
OUT = os.path.join(PROJECT, 'Resources', 'Translations', 'ar.json')

def main():
    phrases = [p for p in open(PHRASES, encoding='utf-8').read().splitlines() if p]
    exact = set(phrases)
    result, problems = {}, []
    for sheet in sorted(glob.glob(os.path.join(PROJECT, 'Tools', 'Localization', 'ar', '*.txt'))):
        for n, line in enumerate(open(sheet, encoding='utf-8').read().splitlines(), 1):
            if not line.strip() or line.startswith('#'):
                continue
            if '|||' not in line:
                problems.append(f'{os.path.basename(sheet)}:{n}: no ||| separator'); continue
            english, arabic = (x.strip() for x in line.split('|||', 1))
            if english in exact or 'exact' in os.path.basename(sheet):
                key = english
            else:
                matches = [p for p in phrases if p.startswith(english)]
                if len(matches) != 1:
                    # Not a screen phrase: kept as is (messages from the code use T.Format keys).
                    key = english
                    if not matches:
                        problems.append(f'{os.path.basename(sheet)}:{n}: kept as a new key: {english[:70]}')
                    else:
                        problems.append(f'{os.path.basename(sheet)}:{n}: {len(matches)} phrases start with: {english[:70]}'); continue
                else:
                    key = matches[0]
            result[key] = arabic

    # Messages from the code: ar/messages/mNN.en.txt and its translation mNN.ar.txt, line for line.
    messages_missing = 0
    for english_file in sorted(glob.glob(os.path.join(PROJECT, 'Tools', 'Localization', 'ar', 'messages', '*.en.txt'))):
        arabic_file = english_file[:-len('.en.txt')] + '.ar.txt'
        english_lines = open(english_file, encoding='utf-8').read().splitlines()
        if not os.path.exists(arabic_file):
            messages_missing += len(english_lines); continue
        arabic_lines = open(arabic_file, encoding='utf-8').read().splitlines()
        if len(arabic_lines) != len(english_lines):
            problems.append(f'{os.path.basename(arabic_file)}: {len(arabic_lines)} lines for {len(english_lines)} messages - not used')
            messages_missing += len(english_lines); continue
        for english, arabic in zip(english_lines, arabic_lines):
            if english.strip() and arabic.strip():
                if english.count('{') != arabic.count('{'):
                    problems.append(f'{os.path.basename(arabic_file)}: values differ in: {english[:70]}')
                result.setdefault(english.strip(), arabic.strip())
    if messages_missing:
        problems.append(f'{messages_missing} code messages have no Arabic sheet yet')

    json.dump(dict(sorted(result.items())), open(OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    todo = [p for p in phrases if p not in result]
    print(f'{len(result)} translations; {len(todo)} of {len(phrases)} screen phrases still in English')
    for p in problems:
        print('  ', p)
    if '--todo' in sys.argv:
        for p in todo:
            print(p)

if __name__ == '__main__':
    main()
