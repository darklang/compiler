"""Realign an `else` that convert_dialect syntax left left of its `if`.

A `| Some _ -> if` arm grows to `| Some(_) -> if`, so an `else` aligned
under that `if` ends up left of it. Usage: realign_else.py CONVERT_DIALECT BEFORE AFTER
"""
import re, subprocess, sys
tool, before, after = sys.argv[1:4]
for _ in range(50):
    r = subprocess.run([tool, 'check', before, after], capture_output=True, text=True)
    if r.returncode == 0:
        print('fixed', after); break
    m = re.search(r"at (\d+):(\d+): expected an expression, found 'else'", r.stderr)
    if not m:
        sys.exit(r.stderr[:2000])
    row, col = int(m[1]) - 1, int(m[2]) - 1
    lines = open(after).read().split('\n')
    for prev in range(row - 1, max(row - 30, -1), -1):
        cols = [k.start() + 1 for k in re.finditer(r'[ (]if ', lines[prev])]
        cols = [c for c in cols if col < c <= col + 4]
        if cols:
            lines[row] = ' ' * (cols[0] - col) + lines[row]
            break
    else:
        sys.exit('no if for line %d' % (row + 1))
    open(after, 'w').write('\n'.join(lines))
else:
    sys.exit('gave up')
