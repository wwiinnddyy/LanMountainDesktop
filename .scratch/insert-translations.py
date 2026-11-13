import json, re, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

base = 'desktop/LanMountainDesktop/Localization/'
zh = json.load(open(base + 'zh-CN.json', encoding='utf-8-sig'))
zh_order = list(zh.keys())
tr = json.load(open('.scratch/translations.json', encoding='utf-8'))

ja = json.load(open(base + 'ja-JP.json', encoding='utf-8-sig'))
ko = json.load(open(base + 'ko-KR.json', encoding='utf-8-sig'))
need = {
    'ja-JP': [k for k in zh_order if k not in ja],
    'ko-KR': [k for k in zh_order if k not in ko],
}
union = set(need['ja-JP']) | set(need['ko-KR'])
assert union == set(tr.keys()), f'翻译表与缺口不一致: 多 {set(tr.keys()) - union}, 少 {union - set(tr.keys())}'

placeholder = re.compile(r'\{\d+(?::[^}]+)?\}')
key_line = re.compile(r'^\s*"([^"]+)"\s*:')

for lang, idx in [('ja-JP', 0), ('ko-KR', 1)]:
    path = base + lang + '.json'
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig' if bom else 'utf-8')
    eol = '\r\n' if '\r\n' in text else '\n'
    lines = text.split(eol)

    existing = set()
    for line in lines:
        m = key_line.match(line)
        if m:
            existing.add(m.group(1))

    def find(key):
        pat = re.compile(r'^\s*"' + re.escape(key) + r'"\s*:')
        for i, line in enumerate(lines):
            if pat.match(line):
                return i
        raise KeyError(key)

    inserted = 0
    for k in need[lang]:
        value = tr[k][idx]
        assert sorted(placeholder.findall(zh[k])) == sorted(placeholder.findall(value)), f'{k} 占位符不匹配: {value}'
        assert zh[k].count('\n') == value.count('\n'), f'{k} 换行数不匹配'
        pos = zh_order.index(k)
        anchor = None
        for prev in reversed(zh_order[:pos]):
            if prev in existing:
                anchor = prev
                break
        line = '    ' + json.dumps(k, ensure_ascii=False) + ':  ' + json.dumps(value, ensure_ascii=False) + ','
        if anchor is None:
            lines.insert(1, line)
        else:
            lines.insert(find(anchor) + 1, line)
        existing.add(k)
        inserted += 1

    # 收尾逗号归一：最后一个键行不带逗号，其余都带
    key_indices = [i for i, line in enumerate(lines) if key_line.match(line)]
    for i in key_indices[:-1]:
        if not lines[i].rstrip().endswith(','):
            lines[i] = lines[i].rstrip() + ','
    last = key_indices[-1]
    if lines[last].rstrip().endswith(','):
        lines[last] = lines[last].rstrip()[:-1]

    out = eol.join(lines)
    pairs = json.loads(out, object_pairs_hook=lambda p: p)
    keys = [k for k, _ in pairs]
    assert len(keys) == len(set(keys)), f'{lang} 出现重复键'
    assert sum(1 for line in lines if key_line.match(line)) == len(keys), f'{lang} 一键一行被破坏'
    assert not [k for k in zh if k not in set(keys)], f'{lang} 仍有缺口'
    with open(path, 'wb') as f:
        f.write(out.encode('utf-8-sig' if bom else 'utf-8'))
    print(f'{lang}: 插入 {inserted} 键, 现共 {len(keys)} 键, 缺口 0, BOM={bom}, EOL={"CRLF" if eol == chr(13)+chr(10) else "LF"}')