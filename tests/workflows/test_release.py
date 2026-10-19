"""Exercise the workflow's real release script against a fake GitHub CLI, offline.

Run: python -m unittest discover -s tests/workflows -v
"""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]
BASH = str(Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Git/bin/bash.exe') if os.name == 'nt' else shutil.which('bash')

GH = r'''#!/usr/bin/env bash
set -eu
echo "$*" >> "$CALLS"
case "$1 $2" in
  'api repos/'*)
    if [[ "$2" == */commits/* ]]; then
      case "$CASE" in
        collision|draft_collision) printf '%040d\n' 2 ;;
        published|changed) printf '%040d\n' 1 ;;
        api_failure) echo 'gh: Forbidden (HTTP 403)' >&2; exit 1 ;;
        *) echo 'gh: Not Found (HTTP 404)' >&2; exit 1 ;;
      esac
    elif [[ "$2" == */releases/latest ]]; then
      if [[ "$CASE" == newer ]]; then echo v0.1.9; else echo 'gh: Not Found (HTTP 404)' >&2; exit 1; fi
    else exit 2; fi ;;
  'release view')
    case "$CASE" in
      draft|draft_collision) echo '{"isDraft":true,"targetCommitish":"0000000000000000000000000000000000000001"}' ;;
      published|changed) echo '{"isDraft":false,"targetCommitish":"0000000000000000000000000000000000000001"}' ;;
      *) exit 1 ;;
    esac ;;
  'release download')
    if [[ "$CASE" == changed ]]; then printf different > existing/ZeroBreach.exe; else cp ZeroBreach.exe existing/ZeroBreach.exe; fi
    if [[ "$CASE" == changed ]]; then (cd existing && sha256sum -t ZeroBreach.exe > SHA256SUMS.txt); else cp SHA256SUMS.txt existing/SHA256SUMS.txt; fi ;;
  'release create'|'release upload'|'release edit') echo "$*" >> "$PUBLISHED" ;;
  *) echo "Unexpected gh call: $*" >&2; exit 2 ;;
esac
'''

JQ = r'''#!/usr/bin/env bash
python -c 'import json,sys; args=sys.argv[1:]; args=[a for a in args if a!="-r"]; value=json.load(open(args[1]))[args[0][1:]]; sys.stdout.write(str(value).lower() if isinstance(value,bool) else value)' "$@"
'''

class ReleaseWorkflowTests(unittest.TestCase):
    def run_case(self, case):
        workflow = Path(os.environ.get('WORKFLOW_FILE', ROOT / '.github/workflows/release.yml')).read_text(encoding='utf-8')
        # Extract the final Bash run block; execute it rather than testing YAML strings.
        script = textwrap.dedent(workflow.split('        run: |\n')[-1])
        with tempfile.TemporaryDirectory(prefix='zb-release-') as directory:
            root = Path(directory)
            (root / 'bin').mkdir()
            (root / 'artifacts').mkdir()
            for name, content in [('gh', GH), ('jq', JQ)]:
                path = root / 'bin' / name
                path.write_text(content, encoding='utf-8', newline='\n')
                path.chmod(0o755)
            exe = b'controlled executable fixture'
            (root / 'artifacts/ZeroBreach.exe').write_bytes(exe)
            (root / 'artifacts/SHA256SUMS.txt').write_text(hashlib.sha256(exe).hexdigest() + '  ZeroBreach.exe\n', encoding='utf-8', newline='\n')
            (root / 'run.sh').write_text(script, encoding='utf-8', newline='\n')
            env = dict(os.environ, CASE=case, GH_REPO='example/test', RELEASE_VERSION='0.1.3', SOURCE_SHA='0' * 39 + '1',
                       CALLS=str(root / 'calls.txt'), PUBLISHED=str(root / 'published.txt'))
            # Let Bash prefix its own PATH so Windows path separators do not interfere.
            result = subprocess.run([BASH, '-c', 'export PATH="$PWD/bin:$PATH"; bash run.sh'], cwd=root, env=env, capture_output=True, text=True, timeout=20)
            published = (root / 'published.txt').read_text() if (root / 'published.txt').exists() else ''
            return result.returncode, published, result.stdout + result.stderr

    def test_new_release_uploads_before_publishing_exact_commit(self):
        code, calls, output = self.run_case('new')
        self.assertEqual(code, 0, output)
        self.assertIn('--target ' + '0' * 39 + '1', calls)
        self.assertLess(calls.index('release create'), calls.index('release edit'))
        self.assertIn('--draft=false --latest=true', calls)

    def test_existing_tag_on_another_commit_never_publishes(self):
        code, calls, _ = self.run_case('collision')
        self.assertNotEqual(code, 0)
        self.assertEqual(calls, '')

    def test_draft_cannot_hide_a_conflicting_existing_tag(self):
        code, calls, _ = self.run_case('draft_collision')
        self.assertNotEqual(code, 0)
        self.assertEqual(calls, '')

    def test_existing_matching_release_is_immutable(self):
        code, calls, output = self.run_case('published')
        self.assertEqual(code, 0, output)
        self.assertEqual(calls, '')

    def test_existing_release_with_different_bytes_is_rejected(self):
        code, calls, _ = self.run_case('changed')
        self.assertNotEqual(code, 0)
        self.assertEqual(calls, '')

    def test_draft_with_no_tag_recovers_and_publishes(self):
        code, calls, output = self.run_case('draft')
        self.assertEqual(code, 0, output)
        self.assertLess(calls.index('release upload'), calls.index('release edit'))

    def test_api_denial_is_not_treated_as_missing_tag(self):
        code, calls, _ = self.run_case('api_failure')
        self.assertNotEqual(code, 0)
        self.assertEqual(calls, '')

    def test_older_completed_build_does_not_replace_newer_latest(self):
        code, calls, output = self.run_case('newer')
        self.assertEqual(code, 0, output)
        self.assertIn('--latest=false', calls)

if __name__ == '__main__':
    unittest.main()
