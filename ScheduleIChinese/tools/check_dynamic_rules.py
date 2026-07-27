#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Validator for dynamic_zh_CN.txt regex translation rules.

Mirrors the plugin's TranslationStore: keys prefixed r:/sr: are .NET-style
regexes anchored as \A(?:pattern)\z; replacements use $1/${name}/$$ tokens.
Checks: regex compiles (Python approximation), replacement group references
exist, and flags rules whose replacement drops capture groups entirely.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

DEFAULT = Path(__file__).resolve().parents[1] / "Translations" / "dynamic_zh_CN.txt"


def split_unescaped(line: str):
    escaped = False
    for i, char in enumerate(line):
        if char == "=" and not escaped:
            return line[:i], line[i + 1:]
        escaped = char == "\\" and not escaped
    return None


def unescape(text: str) -> str:
    out, i = [], 0
    while i < len(text):
        if text[i] == "\\" and i + 1 < len(text):
            nxt = text[i + 1]
            out.append({"n": "\n", "r": "\r", "\\": "\\", "=": "="}.get(nxt, "\\" + nxt))
            i += 2
            if out[-1].startswith("\\") and len(out[-1]) == 2:
                pass
            continue
        out.append(text[i])
        i += 1
    return "".join(out)


RX_GROUP_REF = re.compile(r"\$(\d+)|\$\{(\w+)\}")


def dotnet_to_python(pattern: str):
    """Convert the .NET constructs used by this file into Python equivalents."""
    flags = 0
    # The validator wraps every rule in \A(?:...)\Z, so a nested global (?i)
    # would be illegal in Python. Lift it into compile flags instead.
    if pattern.startswith("(?i)"):
        flags |= re.IGNORECASE
        pattern = pattern[4:]
    elif pattern.startswith("^(?i)"):
        flags |= re.IGNORECASE
        pattern = "^" + pattern[5:]
    # .NET named captures use (?<name>...), Python uses (?P<name>...).
    # The name restriction deliberately avoids rewriting lookbehind (?<= / ?<!).
    pattern = re.sub(r"\(\?<([A-Za-z_]\w*)>", r"(?P<\1>", pattern)
    return pattern, flags


def replacement_group_refs(replacement: str):
    """Yield capture references while respecting the plugin's $$ literal token."""
    i = 0
    while i < len(replacement):
        if replacement[i] != "$" or i + 1 >= len(replacement):
            i += 1
            continue
        if replacement[i + 1] == "$":
            i += 2
            continue
        if replacement[i + 1] == "{":
            close = replacement.find("}", i + 2)
            if close > i + 2:
                yield replacement[i + 2:close]
                i = close + 1
                continue
        match = re.match(r"\d+", replacement[i + 1:])
        if match:
            yield match.group(0)
            i += 1 + len(match.group(0))
            continue
        i += 1


def main() -> int:
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT
    errors = []
    warnings = []
    rules = 0
    for lineno, raw in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not raw or raw.startswith(("#", "//")):
            continue
        pair = split_unescaped(raw)
        if pair is None:
            errors.append(f"line {lineno}: no unescaped '=' separator")
            continue
        key, value = pair
        if key.startswith("r:"):
            pattern = key[2:]
        elif key.startswith("sr:"):
            pattern = key[3:]
        else:
            errors.append(f"line {lineno}: rule key missing r:/sr: prefix: {key[:60]}")
            continue
        pattern = unescape(pattern.strip())
        if len(pattern) >= 2 and pattern[0] == '"' and pattern[-1] == '"':
            pattern = pattern[1:-1]
        replacement = unescape(value)
        if re.search(r"\\\$\$\$\d", replacement):
            errors.append(
                f"line {lineno}: stray backslash before literal-dollar/group token"
            )
        rules += 1
        try:
            python_pattern, flags = dotnet_to_python(pattern)
            compiled = re.compile(r"\A(?:" + python_pattern + r")\Z", flags)
        except re.error as exc:
            errors.append(f"line {lineno}: regex failed to compile: {exc}: {pattern[:80]}")
            continue
        n_groups = compiled.groups
        group_names = set(compiled.groupindex)
        used = set()
        for token in replacement_group_refs(replacement):
            if token.isdigit():
                idx = int(token)
                used.add(idx)
                if idx == 0 or idx > n_groups:
                    errors.append(f"line {lineno}: replacement references group ${idx} but pattern has {n_groups}")
            else:
                if token not in group_names:
                    errors.append(f"line {lineno}: replacement references ${{{token}}} not present in pattern")
                else:
                    used.add(token)
        if n_groups > 0 and not used and not group_names:
            warnings.append(f"line {lineno}: pattern captures {n_groups} group(s) but replacement uses none")
    print(f"file: {path}")
    print(f"rules: {rules}")
    print(f"errors: {len(errors)}")
    for e in errors[:60]:
        print("ERROR:", e)
    print(f"warnings: {len(warnings)}")
    for w in warnings[:30]:
        print("WARN:", w)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
