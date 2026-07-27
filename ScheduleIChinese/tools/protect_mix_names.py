# -*- coding: utf-8 -*-
"""Protect game-generated mix names and revert quality-tier product translations.

The mix name generator combines adjective + noun pools found in
sharedassets0.assets. Every A+N combo is a potential custom product name
and must stay English:
  - delete any zh_CN.txt entry whose key is an A+N combo;
  - register all combos as identity entries in preserve_names.txt.
Also delete quality-tier product entries (Heavenly/Premium/Standard/Poor
+ drug), per user request to keep quality tiers English.
"""
import sys

ADJECTIVES = ['Aspen', 'Tokyo', 'California', 'Super', 'Mega', 'Granddaddy',
              'White', 'Dark', 'Sweet', 'Island', 'Miracle', 'Death', 'Pink',
              'Bio', 'Girl Scout', 'Gorilla', 'Fruity', 'Wedding', 'Strawberry',
              'Banana', 'Ice Cream', 'Purple', 'Afghan', 'Nightmare', 'Dream',
              'Ultra', 'Sexy', 'Hairy', 'Shiny', 'Big', 'Fat', 'Thick',
              'Extreme', 'Stinky', 'Slimy']
NOUNS = ['Death', 'Balls', 'Crystal', 'Puke', 'Stink', 'Cock', 'Cookies',
         'Haze', 'Punch', 'Gold', 'Cheese', 'Diesel', 'Bud', 'Thunderfuck',
         'Ghost', 'Dick', 'Cake', 'Diamond', 'Balls', 'Urkle', 'Durkle',
         'Monkey', 'Piss', 'Fuel', 'Rhino', 'McLovin', 'Assblaster',
         'Express', 'Mint', 'Crack', 'Ass', 'Fruit', 'Shart', 'Smegma',
         'Splooge', 'Cum', 'Queef', 'Grool', 'Slime']

QUALITY_PREFIX = ('Heavenly ', 'Premium ', 'Standard ', 'Poor ')
QUALITY_SUFFIX = ('Cocaine', 'Magic Mushroom', 'Marijuana', 'Methamphetamine',
                  '{{A}}')


def key_of(line):
    esc = False
    for i, c in enumerate(line):
        if c == '=' and not esc:
            return line[:i] if i > 0 else None
        if c == '\\' and not esc:
            esc = True
        else:
            esc = False
    return None


def main():
    zh, preserve = sys.argv[1:3]

    combos = {a + ' ' + n for a in ADJECTIVES for n in NOUNS}

    kept, removed_mix, removed_q = [], [], []
    for raw in open(zh, encoding='utf-8'):
        line = raw.rstrip('\n')
        k = key_of(line)
        if k is not None:
            ku = k.replace('\\=', '=').replace('\\#', '#')
            if ku in combos:
                removed_mix.append(line)
                continue
            if ku.startswith(QUALITY_PREFIX) and ku.endswith(QUALITY_SUFFIX):
                removed_q.append(line)
                continue
        kept.append(line)
    with open(zh, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(kept) + '\n')

    existing = set()
    for raw in open(preserve, encoding='utf-8'):
        line = raw.rstrip('\n')
        if line and not line.startswith('#') and '=' in line:
            existing.add(line.split('=', 1)[0])
    added = 0
    with open(preserve, 'a', encoding='utf-8', newline='\n') as f:
        f.write('\n# 游戏混名生成器词池组合（形容词+名词），全部保持英文\n')
        for name in sorted(combos):
            if name in existing:
                continue
            f.write(f'{name}={name}\n')
            added += 1

    print(f'zh_CN: removed {len(removed_mix)} mix-name entries, '
          f'{len(removed_q)} quality-tier entries')
    for l in removed_mix:
        print('  mix:', l)
    for l in removed_q:
        print('  tier:', l)
    print(f'preserve_names: added {added} combo protections')


if __name__ == '__main__':
    main()
