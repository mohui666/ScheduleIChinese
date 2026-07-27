# -*- coding: utf-8 -*-
"""Final split of removed single-token entries into DENY and RESTORE.

DENY when the game can plausibly read the string back:
  1. case-insensitive match with an enum member name from the game's own
     assemblies (Assembly-CSharp + UnityEngine.CoreModule, dumped by
     tools/EnumDump -> enum_members_game.txt);
  2. listed in preserve_names.txt (NPC/strain names stay English);
  3. already covered by effects_zh_CN.txt;
  4. junk keys: hex color blobs or any token containing a digit;
  5. known console commands (typed into the dev console, parsed as-is).

Writes:
  tools/restored_entries.txt  - key=value lines to re-add to zh_CN.txt
  Translations/deny_keys.txt  - full runtime denylist (enum members + commands)
"""
import re
import sys

JUNK = re.compile(r'^(?:[0-9A-Fa-f]{6,8}|[A-Za-z0-9_]*\d[A-Za-z0-9_]*)$')

CONSOLE_COMMANDS = {
    'addemployee', 'addxp', 'bind', 'changebalance', 'changecash', 'clearbinds',
    'clearinventory', 'cleartrash', 'clearwanted', 'disable', 'disablenpcasset',
    'enable', 'endtutorial', 'forcesleep', 'freecam', 'give', 'growplants',
    'hidefps', 'hideui', 'lowerwanted', 'packageprodcut', 'playcutscene',
    'raisewanted', 'savegame', 'settime', 'teleport',
}


def parse_kv_lines(path, skip_comments=True):
    for raw in open(path, encoding='utf-8'):
        line = raw.rstrip('\n')
        if not line or (skip_comments and (line.startswith('#') or line.startswith('//'))):
            continue
        if '=' in line:
            yield line


def main():
    report, trans_dir, enum_file = sys.argv[1:4]

    # Only enum types whose names the game plausibly displays AND reads back:
    # input rebinding, settings panels, quality/rank/region/day labels,
    # character creator, UI popup responses, employees, casino, quests.
    DANGEROUS_TYPES = (
        'ScheduleOne.GameInput', 'ScheduleOne.DevUtilities',
        'ScheduleOne.ItemFramework.EQuality', 'ScheduleOne.Levelling.ERank',
        'ScheduleOne.GameTime.EDay', 'ScheduleOne.Map.EMapRegion',
        'ScheduleOne.AvatarFramework.Customization', 'ScheduleOne.Clothing',
        'ScheduleOne.UI', 'ScheduleOne.Employees', 'ScheduleOne.Casino',
        'ScheduleOne.Quests', 'UnityEngine.KeyCode',
    )
    enum_names = set()
    deny_file_members = []
    for line in open(enum_file, encoding='utf-8'):
        w = line.strip()
        if not w or '.' not in w:
            continue
        tname, _, member = w.rpartition('.')
        if tname.startswith(DANGEROUS_TYPES):
            enum_names.add(member.lower())
            deny_file_members.append(member)
    enum_names |= CONSOLE_COMMANDS

    preserved = {l.split('=', 1)[0] for l in parse_kv_lines(f'{trans_dir}/preserve_names.txt')}
    effects = {l.split('=', 1)[0] for l in parse_kv_lines(f'{trans_dir}/effects_zh_CN.txt')}

    deny_keys = set()
    restore = []
    n_junk = n_preserve = n_effects = 0
    for raw in open(report, encoding='utf-8'):
        raw = raw.rstrip('\n')
        if '\t' not in raw:
            continue
        kv = raw.split('\t', 1)[1]
        if '=' not in kv:
            continue
        key = kv.split('=', 1)[0]
        if key.lower() in enum_names:
            deny_keys.add(key)
        elif key in preserved:
            n_preserve += 1
        elif key in effects:
            n_effects += 1
        elif JUNK.match(key):
            n_junk += 1
        else:
            restore.append(kv)

    # Runtime denylist: all members of the dangerous enum types + commands.
    all_deny = sorted(set(deny_file_members))
    with open(f'{trans_dir}/deny_keys.txt', 'w', encoding='utf-8', newline='\n') as f:
        f.write('# 禁止翻译的键：游戏枚举成员名（会被代码读回）+ 控制台命令。\n')
        f.write('# 由 tools/EnumDump 与 tools/restore_safe_keys.py 生成，加载时按不区分大小写拒绝。\n')
        for k in all_deny:
            f.write(k + '\n')
        for k in sorted(CONSOLE_COMMANDS):
            f.write(k + '\n')

    with open('tools/restored_entries.txt', 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(restore) + '\n')

    print(f'restore: {len(restore)}  deny(enum/cmd): {len(deny_keys)}  '
          f'preserved: {n_preserve}  effects: {n_effects}  junk: {n_junk}')
    print('deny sample:', sorted(deny_keys)[:40])
    print('restore sample:', [r.split("=", 1)[0] for r in restore[:40]])


if __name__ == '__main__':
    main()
