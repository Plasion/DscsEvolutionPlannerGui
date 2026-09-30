# -*- coding: utf-8 -*-
"""生成 Data/PinyinTable.json —— C# 侧做拼音模糊匹配所用的数据表。

这是**开发期**脚本（只在数据变化时手动跑一次），界面运行时不会调用 Python：
生成出来的 JSON 会以 Content 形式随程序发布，C# 直接读它。

用法：
    python tools/gen_pinyin_table.py [--data <data.csv>] [--out <PinyinTable.json>]

输出结构：
{
  "version": 1,
  "keyHash": "8 位十六进制",   # 参与匹配的键集合的 FNV-1a 32 位哈希（C# 端核对数据是否同源）
  "phrases": { "亚古兽": ["ya","gu","shou"], ... },   # 名字/别名/世代/技能名 → 整词拼音
  "chars":   { "亚": "ya", ... },                      # 逐字拼音（用户输入的兜底）
  "syllables": ["zhuang", "shang", ...]                # 音节表（长度降序），用于切分纯拼音输入
}
"""
import argparse
import csv
import json
from pathlib import Path

from pypinyin import Style, pinyin


def find_data_csv(explicit: str | None) -> Path:
    if explicit:
        p = Path(explicit).expanduser().resolve()
        if not p.is_file():
            raise SystemExit(f'找不到 data.csv：{p}')
        return p
    here = Path(__file__).resolve()
    for base in [here.parent, *here.parents]:
        for cand in (base / 'data.csv', base / 'py' / 'data.csv'):
            if cand.is_file():
                return cand
    raise SystemExit('未找到 data.csv，请用 --data 指定')


def quoted(text: str) -> list[str]:
    """取出 Python 字面量里的单引号字符串："['a', 'b']" -> ['a', 'b']"""
    out, buf, inside = [], [], False
    for ch in text:
        if ch == "'":
            if inside:
                out.append(''.join(buf))
                buf.clear()
            inside = not inside
        elif inside:
            buf.append(ch)
    return out


def to_syllables(text: str) -> list[str]:
    """整词转拼音，丢弃声调（与 planner.py 用的 mohu 行为一致）。"""
    if not text:
        return []
    return [item[0] for item in pinyin(text, style=Style.NORMAL, errors='default')]


def fnv1a32(keys) -> str:
    h = 0x811C9DC5
    for byte in '\n'.join(sorted(keys)).encode('utf-8'):
        h = ((h ^ byte) * 0x01000193) & 0xFFFFFFFF
    return f'{h:08x}'


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default=None)
    ap.add_argument('--out', default=None)
    args = ap.parse_args()

    data_csv = find_data_csv(args.data)
    out_path = (Path(args.out).expanduser().resolve() if args.out
                else Path(__file__).resolve().parents[1] / 'Data' / 'PinyinTable.json')

    keys: list[str] = []
    generations: set[str] = set()
    skills: set[str] = set()

    with data_csv.open(mode='r', encoding='utf-8', newline='') as f:
        for row in csv.DictReader(f):
            keys.append(row['名字'])
            if row['别名']:
                keys.append(row['别名'])
            generations.add(row['世代'])
            for skill in quoted(row['继承技']):
                skills.add(skill.split('.', 1)[1].translate(str.maketrans('ⅠⅡⅢ', '123')))

    keys.extend(sorted(generations))
    keys.extend(sorted(skills))
    keys = sorted(set(keys))

    phrases = {k: to_syllables(k) for k in keys}

    # 逐字表：词典里用到的字 + 常用汉字区全量（用户输入可能含词典外的字，
    # 例如把「亚古兽」打成「压古兽」，只有全量字表才能让拼音分不退化）
    cjk = [chr(code) for code in range(0x4E00, 0x9FA6)]
    readings = pinyin(cjk, style=Style.NORMAL, errors='default')
    chars: dict[str, str] = {ch: item[0] for ch, item in zip(cjk, readings) if item and item[0]}
    for k in keys:
        for ch in k:
            if ch not in chars:
                syl = to_syllables(ch)
                chars[ch] = syl[0] if syl else ch

    # 音节表：优先用 mohu 内置表（更全），没有 mohu 时用词典里出现过的音节兜底
    try:
        from mohu.pinyin import PinyinConverter
        syllables = list(PinyinConverter().pinyin_syllables)
    except Exception as exc:  # noqa: BLE001
        print(f'提示：未取到 mohu 音节表（{exc}），改用词典音节兜底')
        syllables = sorted({s for lst in phrases.values() for s in lst})

    payload = {
        'version': 1,
        'keyHash': fnv1a32(keys),
        'source': str(data_csv),
        'phrases': phrases,
        'chars': chars,
        'syllables': sorted(syllables, key=len, reverse=True),
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, ensure_ascii=False, indent=1), encoding='utf-8')
    print(f'已写入 {out_path}')
    print(f'  键 {len(keys)} 个 / 字 {len(chars)} 个 / 音节 {len(syllables)} 个 / keyHash {payload["keyHash"]}')
    print(f'  文件大小 {out_path.stat().st_size / 1024:.0f} KB')


if __name__ == '__main__':
    main()
