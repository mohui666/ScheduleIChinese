# -*- coding: utf-8 -*-
"""Scan translation files and classify entries whose keys look like variable names."""
import re
import sys

IDENT = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')


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


def parse(path):
    keys = []
    for raw in open(path, encoding='utf-8'):
        line = raw.rstrip('\n')
        if not line or line.startswith('#') or line.startswith('//'):
            continue
        esc = False
        eq = -1
        for i, c in enumerate(line):
            if c == '=' and not esc:
                eq = i
                break
            if c == '\\' and not esc:
                esc = True
            else:
                esc = False
        if eq <= 0:
            continue
        keys.append((unescape(line[:eq]), unescape(line[eq + 1:])))
    return keys


for f in sys.argv[1:]:
    keys = parse(f)
    single = [(k, v) for k, v in keys if IDENT.match(k)]
    print(f, 'total:', len(keys), 'single-token:', len(single))
    if single:
        print('  ', [k for k, v in single])
