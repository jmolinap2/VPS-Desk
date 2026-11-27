"""Genera las notas de la versión (lo que la app muestra al ofrecer una actualización).

Si se indican notas a mano, se usan tal cual. Si no, se toman los commits `feat` y `fix` publicados desde la
versión anterior (el `version.json` del canal guarda el commit de cada publicación en el campo `commit`).
"""
import argparse
import json
import re
import subprocess
import urllib.request
from pathlib import Path

MAX_LINES = 8
MAX_LINE = 160
FALLBACK = 'Mejoras internas y correcciones menores.'
_CONVENTIONAL = re.compile(r'^(?:feat|fix)(?:\([^)]*\))?!?:\s*(?P<text>\S.*)$', re.IGNORECASE)
_SHA = re.compile(r'^[0-9a-f]{40}$')


def notes_from_subjects(subjects):
    lines = []
    for subject in subjects:
        match = _CONVENTIONAL.match(subject.strip())
        if not match:
            continue
        text = match.group('text').strip().rstrip('.')
        text = text[0].upper() + text[1:]
        if len(text) > MAX_LINE:
            text = text[:MAX_LINE - 1].rstrip() + '…'
        line = f'• {text}'
        if line not in lines:
            lines.append(line)
    if not lines:
        return FALLBACK
    hidden = len(lines) - MAX_LINES
    lines = lines[:MAX_LINES]
    if hidden > 0:
        lines.append(f'• …y {hidden} cambios más.')
    return '\n'.join(lines)


def previous_commit(manifest_url):
    """Commit de la versión publicada antes de esta, o None si el canal no lo guarda o no responde."""
    try:
        request = urllib.request.Request(manifest_url, headers={'Cache-Control': 'no-cache', 'User-Agent': 'VpsDesk-Release'})
        with urllib.request.urlopen(request, timeout=30) as response:
            document = json.loads(response.read(1024 * 1024).decode('utf-8-sig'))
    except (OSError, ValueError):
        return None
    commit = document.get('commit') if isinstance(document, dict) else None
    return commit if isinstance(commit, str) and _SHA.match(commit) else None


def git(*args):
    return subprocess.run(['git', *args], check=True, capture_output=True, text=True).stdout


def subjects_since(previous, head):
    if previous and subprocess.run(['git', 'cat-file', '-e', f'{previous}^{{commit}}'], capture_output=True).returncode == 0:
        return git('log', '--no-merges', '--format=%s', f'{previous}..{head}').splitlines()
    # Sin commit anterior conocido (primera publicación con este mecanismo): los últimos commits.
    return git('log', '--no-merges', '--format=%s', '-n', '10', head).splitlines()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--head', required=True)
    parser.add_argument('--manual', default='')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    manual = args.manual.strip()
    notes = manual if manual else notes_from_subjects(subjects_since(previous_commit(args.manifest), args.head))
    Path(args.output).write_text(notes, encoding='utf-8')
    print(notes)


if __name__ == '__main__':
    main()
