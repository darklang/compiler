#!/usr/bin/env python3
"""Copy packages/ and LICENSE.md of darklang/dark at a revision into upstream/, unchanged.

Usage: sync-upstream.py DARK_CHECKOUT [REVISION]
Records the revision and its packages tree id in porting/sources.yaml and rewrites
porting/upstream-sources.list. Compiler decisions stay in porting/upstream-annotations.tsv;
a build reports any annotation whose item the new revision no longer has.
"""
from pathlib import Path
import io
import re
import shutil
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    if len(sys.argv) not in (2, 3):
        raise SystemExit(__doc__)
    checkout = Path(sys.argv[1]).resolve()
    config = ROOT / 'porting/sources.yaml'
    text = config.read_text()
    revision = sys.argv[2] if len(sys.argv) == 3 else re.search(r'^upstream_revision: (\S+)$', text, re.M)[1]
    git = lambda *args: subprocess.check_output(['git', '-C', str(checkout), *args])
    revision = git('rev-parse', revision + '^{commit}').decode().strip()
    tree = git('rev-parse', revision + ':packages').decode().strip()
    shutil.rmtree(ROOT / 'upstream', ignore_errors=True)
    with tarfile.open(fileobj=io.BytesIO(git('archive', revision, 'packages', 'LICENSE.md'))) as archive:
        archive.extractall(ROOT / 'upstream', filter='data')
    paths = sorted(str(p.relative_to(ROOT / 'upstream')) for p in (ROOT / 'upstream/packages').rglob('*.dark'))
    (ROOT / 'porting/upstream-sources.list').write_text(
        '# Original interpreter paths at the revision in porting/sources.yaml.\n' + ''.join(p + '\n' for p in paths))
    text = re.sub(r'^upstream_revision: .*$', 'upstream_revision: ' + revision, text, flags=re.M)
    text = re.sub(r'^upstream_packages_tree: .*$', 'upstream_packages_tree: ' + tree, text, flags=re.M)
    config.write_text(text)
    print(f'upstream/ now holds {len(paths)} sources at {revision}')


if __name__ == '__main__':
    main()
