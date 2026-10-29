"""前台 Razor 的可見文字抽取與替換工具（i18n 遷移用）。

只處理元素文字節點；跳過屬性值、Razor 表達式與既有佔位，並以分段處理
完全不去碰 @code 區塊（避免巢狀大括號造成還原錯位）。
輸出格式自我說明：@UiText.T("鍵", "原文")。
"""
import io
import json
import re
import sys

CODE_BLOCK = re.compile(r'@code\s*\{.*?\n\}', re.S)
RAZOR_COMMENT = re.compile(r'@\*.*?\*@', re.S)
TEXT_NODE = re.compile(r'(>)([^<>{}@\n]+)(<)')
EXISTING = re.compile(r'@UiText\.T\("[^"]+",\s*"[^"]*"\)')
MARK = '\uE000'


def replace_in_markup(chunk, prefix, pairs, seen, counter):
    held = []

    def hold(match):
        held.append(match.group(0))
        return '{0}'.format(len(held) - 1)

    masked = RAZOR_COMMENT.sub(hold, chunk)
    masked = EXISTING.sub(hold, masked)

    def replace(match):
        text = match.group(2).strip()
        if not text or any(ch in text for ch in '@{}()='):
            return match.group(0)
        if MARK in text or not re.search(r'[\u4e00-\u9fff]', text):
            return match.group(0)
        if text in seen:
            key = seen[text]
        else:
            counter[0] += 1
            key = "{0}.{1:02d}".format(prefix, counter[0])
            seen[text] = key
            pairs.append((key, text))
        escaped = text.replace('"', '\\"')
        return match.group(1) + '@UiText.T("' + key + '", "' + escaped + '")' + match.group(3)

    updated = TEXT_NODE.sub(replace, masked)
    for index, original in enumerate(held):
        updated = updated.replace('{0}'.format(index), original)
    return updated


def extract(path, prefix):
    source = io.open(path, encoding='utf-8').read()
    pairs = []
    seen = {}
    counter = [0]
    parts = CODE_BLOCK.split(source)
    blocks = CODE_BLOCK.findall(source)
    output = []
    for index, part in enumerate(parts):
        output.append(replace_in_markup(part, prefix, pairs, seen, counter))
        if index < len(blocks):
            output.append(blocks[index])
    return ''.join(output), pairs


def main():
    targets = json.load(io.open(sys.argv[1], encoding='utf-8'))
    catalog = {}
    outputs = []
    for path, prefix in targets:
        updated, pairs = extract(path, prefix)
        outputs.append((path, updated))
        catalog[prefix] = pairs
    if len(sys.argv) > 2 and sys.argv[2] == '--write':
        for path, updated in outputs:
            io.open(path, 'w', encoding='utf-8').write(updated)
    json.dump(catalog, io.open('ui-text-catalog.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print("keys: {0}".format(sum(len(v) for v in catalog.values())))


if __name__ == '__main__':
    main()
