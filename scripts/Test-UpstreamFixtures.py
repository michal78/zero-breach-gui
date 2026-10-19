"""Run the signed scanner against isolated synthetic browser paths; no indicator is contacted.

Developer check: python scripts/Test-UpstreamFixtures.py PYTHON_EXE VERIFIED_SCANNER
Outputs reports under ignored .tools/upstream-fixtures for the C# parser smoke check.
"""
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys

root = Path('.tools/upstream-fixtures').resolve()
root.mkdir(parents=True, exist_ok=True)
python, scanner = map(lambda p: str(Path(p).resolve()), sys.argv[1:3])
indicator = 'controlium.net'
for positive in (False, True):
    case = root / ('positive' if positive else 'negative')
    local = case / 'Local'
    roaming = case / 'Roaming'
    profile = local / 'Google/Chrome/User Data/Default'
    profile.mkdir(parents=True, exist_ok=True)
    roaming.mkdir(exist_ok=True)
    (case / 'Temp').mkdir(exist_ok=True)
    with sqlite3.connect(profile / 'History') as con:
        con.execute('CREATE TABLE IF NOT EXISTS urls(url TEXT, title TEXT, last_visit_time INTEGER)')
        con.execute('DELETE FROM urls')
        con.execute('INSERT INTO urls VALUES(?,?,?)', ('https://example.test/', 'Synthetic reference ' + indicator if positive else 'Synthetic normal record', 13380163200000000))
        con.execute('CREATE TABLE IF NOT EXISTS downloads(current_path TEXT, tab_url TEXT, referrer TEXT)')
    env = dict(os.environ, LOCALAPPDATA=str(local), APPDATA=str(roaming), USERPROFILE=str(case), HOME=str(case), TEMP=str(case / 'Temp'), TMP=str(case / 'Temp'))
    for quick in (True, False):
        report = root / f'{"positive" if positive else "negative"}-{"quick" if quick else "full"}.json'
        result = subprocess.run([python, '-I', '-S', '-B', '-X', 'utf8', scanner, '--json', str(report)] + (['--quick'] if quick else []), env=env, capture_output=True, timeout=180)
        if result.returncode not in (0, 2) or not report.exists():
            raise RuntimeError('Signed scanner fixture run did not produce a report')
        document = json.loads(report.read_text(encoding='utf-8'))
        hits = [h for m in document['modules'].values() for h in m['hits']]
        synthetic = [h for h in hits if h['indicator'] == indicator and 'upstream-fixtures' in h['location']]
        if positive and not any(h['evidence'] == 'referenced' for h in synthetic):
            raise RuntimeError('Expected synthetic saved-reference finding missing')
        if not positive and synthetic:
            raise RuntimeError('Unexpected synthetic indicator match')
        print(f'PASS: {report.name}; exit={result.returncode}; synthetic reference={len(synthetic)}', flush=True)
