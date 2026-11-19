import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import android_release_version as release


class Response:
    def __init__(self, data):
        self.data = data
        self.position = 0

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def read(self, size=-1):
        end = len(self.data) if size < 0 else min(len(self.data), self.position + size)
        data = self.data[self.position:end]
        self.position = end
        return data


class ReleaseVersionTests(unittest.TestCase):
    def test_allocate_next_version_from_published_channel(self):
        raw = b'{"versionCode":10}'
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / 'project.xml'
            output = Path(directory) / 'output.txt'
            project.write_text('<Project><PropertyGroup><ApplicationVersion>9</ApplicationVersion>'
                               '<ApplicationDisplayVersion>1.0.9</ApplicationDisplayVersion>'
                               '</PropertyGroup></Project>', encoding='utf-8')
            args = ['release', 'prepare', '--manifest', 'https://example.test/version.json',
                    '--project', str(project), '--output', str(output)]
            with patch.object(sys, 'argv', args), patch.object(release.urllib.request, 'urlopen', return_value=Response(raw)):
                release.main()
            self.assertIn('code=11\n', output.read_text(encoding='utf-8'))
            self.assertIn('name=1.0.9+11\n', output.read_text(encoding='utf-8'))

    def test_refuses_stale_or_changed_channel(self):
        release.ensure_can_publish(11, (10, 'same'), 'same')
        with self.assertRaises(ValueError):
            release.ensure_can_publish(10, (10, 'same'), 'same')
        with self.assertRaises(ValueError):
            release.ensure_can_publish(11, (10, 'changed'), 'same')

    def test_display_name_does_not_go_back_when_main_is_behind(self):
        raw = b'{"versionCode":10,"versionName":"1.0.9"}'
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / 'project.xml'
            output = Path(directory) / 'output.txt'
            project.write_text('<Project><PropertyGroup><ApplicationVersion>9</ApplicationVersion>'
                               '<ApplicationDisplayVersion>1.0.8</ApplicationDisplayVersion>'
                               '</PropertyGroup></Project>', encoding='utf-8')
            args = ['release', 'prepare', '--manifest', 'https://example.test/version.json',
                    '--project', str(project), '--output', str(output)]
            with patch.object(sys, 'argv', args), patch.object(release.urllib.request, 'urlopen', return_value=Response(raw)):
                release.main()
            self.assertIn('name=1.0.9+11\n', output.read_text(encoding='utf-8'))

    def test_verify_download_and_reject_bad_hash(self):
        apk = b'fake APK bytes'
        manifest = json.dumps({'versionCode': 11, 'apkUrl': 'vpsdesk-11.apk',
                               'sha256': hashlib.sha256(apk).hexdigest()}).encode()
        with patch.object(release.urllib.request, 'urlopen', side_effect=[Response(manifest), Response(apk)]):
            release.verify_release('https://example.test/version.json', 11)
        with patch.object(release.urllib.request, 'urlopen', side_effect=[Response(manifest), Response(b'wrong')]):
            with self.assertRaises(ValueError):
                release.verify_release('https://example.test/version.json', 11)
        with patch.object(release.urllib.request, 'urlopen', return_value=Response(manifest)):
            with self.assertRaises(ValueError):
                release.verify_release('https://example.test/version.json', 11, '0' * 64)

    def test_rejects_insecure_channel(self):
        with self.assertRaises(ValueError):
            release.read_channel('http://example.test/version.json')


if __name__ == '__main__':
    unittest.main()
