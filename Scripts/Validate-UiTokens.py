#!/usr/bin/env python3
"""Checks the migrated batch 2/3 views. Accepts explicit .axaml paths as arguments."""
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / 'src/VpsDesk.UI'
VIEWS = ['ProjectsView', 'StorageView', 'FilesView', 'TerminalView', 'SecurityView',
         'SettingsView', 'EnvironmentView', 'DashboardView', 'BackupsView', 'LocalBackupsView']
VIEWS += ['Mobile' + name for name in VIEWS[:8]]
VIEWS += ['MobileBackupsView', 'MobileFileEditor', 'MobileEnvVariables', 'MobileStyles']

def main():
    paths = [Path(p) for p in sys.argv[1:]] or [UI / 'Views' / (name + '.axaml') for name in VIEWS]
    translations = [json.loads((UI / 'Localization/Languages' / lang / 'Strings.json').read_text(encoding='utf-8'))
                    for lang in ['es-ES', 'en-US']]
    errors = []
    for key in translations[0].keys() ^ translations[1].keys():
        errors.append('Translation key missing in one language: ' + key)
    resources = set(translations[0])
    for path in (UI / 'Themes').glob('*.axaml'):
        resources.update(re.findall(r'x:Key="([^"}]+)"', path.read_text(encoding='utf-8')))
    for path in paths:
        text = path.read_text(encoding='utf-8')
        try:
            ET.fromstring(text)
        except ET.ParseError as error:
            errors.append(f'{path.name}: invalid XML: {error}')
        # Comments may document the old palette; only executable attributes matter.
        text = re.sub(r'<!--[\s\S]*?-->', '', text)
        for pattern, label in [(r'="#[0-9a-fA-F]{6,8}"', 'literal color'),
                               (r'(?:FontSize|CornerRadius|Duration)="[0-9]', 'literal visual value'),
                               (r'Property="(?:FontSize|CornerRadius)" Value="[0-9]', 'literal setter'),
                               (r'(?:Content|Text)="(?:[↑←✕▸▤›↻]|&#(?:8635|10005);)"', 'Unicode icon')]:
            if re.search(pattern, text):
                errors.append(f'{path.name}: {label}')
        local = set(re.findall(r'x:Key="([^"}]+)"', text))
        for key in re.findall(r'\{(?:Dynamic|Static)Resource ([^}]+)\}', text):
            if key not in resources | local:
                errors.append(f'{path.name}: missing resource {key}')
        for button in re.findall(r'<Button\b[^>]*>[\s\S]*?</Button>', text):
            if 'Classes="btn-icon"' in button and ('AutomationProperties.Name=' not in button or 'ToolTip.Tip=' not in button):
                errors.append(f'{path.name}: icon button missing accessible name or tooltip')
    if errors:
        print('\n'.join(errors))
        return 1
    print(f'PASS: {len(paths)} views; XML, tokens, resource references, icon names and es/en key parity.')
    return 0

if __name__ == '__main__':
    sys.exit(main())
