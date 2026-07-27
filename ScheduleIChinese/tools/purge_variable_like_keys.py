# -*- coding: utf-8 -*-
"""Remove entries whose keys look like variable/identifier names.

A key is considered variable-like when it is a single bare alphanumeric token
(no spaces or punctuation), e.g. console commands (addxp), key names
(Backspace), enum values (Standard, Heavenly), color hex blobs (B2FF99FF).
These strings are read back by game code, so translating them breaks logic.
Removed lines are appended to tools/removed_variable_like_entries.txt.
"""
import re
import sys

TOKEN = re.compile(r'^[A-Za-z0-9_]+$')


def unescape(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            out.append({'n': '\n', 'r': '\r', '\\': '\\', '=': '='}.get(n, '\\' + n))
            i += 2
        else:
            out.append(c)
            i += 1
    return ''.join(out)


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


def purge(path, report):
    kept = []
    removed = 0
    with open(path, encoding='utf-8') as f:
        for raw in f:
            line = raw.rstrip('\n')
            stripped = line.strip()
            if not stripped or stripped.startswith('#') or stripped.startswith('//'):
                kept.append(line)
                continue
            k = key_of(line)
            if k is None:
                kept.append(line)
                continue
            if TOKEN.match(unescape(k)):
                removed += 1
                report.write(f'{path}\t{line}\n')
                continue
            kept.append(line)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(kept) + '\n')
    print(f'{path}: removed {removed}, kept {len(kept)} lines')


if __name__ == '__main__':
    with open('tools/removed_variable_like_entries.txt', 'w', encoding='utf-8') as rep:
        for p in sys.argv[1:]:
            purge(p, rep)
