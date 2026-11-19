"""Version and concurrency checks shared by Android release publishers."""
import argparse
import hashlib
import json
import re
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path


def read_channel(url):
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != 'https' or not parsed.netloc:
        raise ValueError('La URL del canal debe usar HTTPS.')
    request = urllib.request.Request(url, headers={'Cache-Control': 'no-cache', 'User-Agent': 'VpsDesk-Release'})
    with urllib.request.urlopen(request, timeout=30) as response:
        raw = response.read(1024 * 1024 + 1)
    if len(raw) > 1024 * 1024:
        raise ValueError('El manifiesto del canal es demasiado grande.')
    document = json.loads(raw.decode('utf-8-sig'))
    code = document.get('versionCode')
    if isinstance(code, bool) or not isinstance(code, int) or code < 1:
        raise ValueError('El canal no contiene un versionCode válido.')
    name = document.get('versionName')
    return code, hashlib.sha256(raw).hexdigest(), name if isinstance(name, str) else None


def ensure_can_publish(candidate, channel, expected_digest=None):
    code, digest = channel[:2]
    if expected_digest is not None and digest != expected_digest:
        raise ValueError('El canal cambió durante la compilación. Repite la publicación.')
    if candidate <= code:
        raise ValueError(f'La versión {candidate} no supera la publicada ({code}).')


def verify_release(url, expected_code, expected_apk_sha=None):
    request = urllib.request.Request(url, headers={'Cache-Control': 'no-cache', 'User-Agent': 'VpsDesk-Release'})
    with urllib.request.urlopen(request, timeout=30) as response:
        raw = response.read(1024 * 1024 + 1)
    if len(raw) > 1024 * 1024:
        raise ValueError('El manifiesto del canal es demasiado grande.')
    document = json.loads(raw.decode('utf-8-sig'))
    if document.get('versionCode') != expected_code:
        raise ValueError('El canal no anuncia la versión recién publicada.')
    apk_url = urllib.parse.urljoin(url, document.get('apkUrl', ''))
    parsed = urllib.parse.urlparse(apk_url)
    if parsed.scheme != 'https' or not parsed.netloc:
        raise ValueError('La URL del APK publicado debe usar HTTPS.')
    expected = document.get('sha256')
    if not isinstance(expected, str) or len(expected) != 64:
        raise ValueError('El manifiesto no contiene un SHA-256 válido.')
    if expected_apk_sha and expected.lower() != expected_apk_sha.lower():
        raise ValueError('El APK anunciado no coincide con el paquete preparado.')
    digest = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(urllib.request.Request(apk_url, headers={'User-Agent': 'VpsDesk-Release'}), timeout=120) as response:
        while block := response.read(1024 * 1024):
            size += len(block)
            if size > 100 * 1024 * 1024:
                raise ValueError('El APK publicado supera el tamaño permitido para verificarlo.')
            digest.update(block)
    if digest.hexdigest().lower() != expected.lower():
        raise ValueError('La huella del APK publicado no coincide con el manifiesto.')
    print(f'Versión {expected_code} publicada y APK verificado ({size} bytes).')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['prepare', 'check', 'verify'])
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--project')
    parser.add_argument('--output')
    parser.add_argument('--code', type=int)
    parser.add_argument('--digest')
    parser.add_argument('--sha256')
    args = parser.parse_args()
    if args.command == 'verify':
        if args.code is None:
            parser.error('verify requiere --code')
        verify_release(args.manifest, args.code, args.sha256)
        return
    channel = read_channel(args.manifest)
    if args.command == 'prepare':
        if not args.project or not args.output:
            parser.error('prepare requiere --project y --output')
        project = ET.parse(args.project).getroot()
        base_code = int(project.findtext('./PropertyGroup/ApplicationVersion') or '0')
        base_name = project.findtext('./PropertyGroup/ApplicationDisplayVersion') or '1.0'
        # A manually published release may have a newer display version than main.
        published_name = (channel[2] or '').split('+', 1)[0]
        def version_parts(value):
            return tuple(int(part) for part in value.split('.')) if re.fullmatch(r'\d+(?:\.\d+){1,3}', value) else ()
        if not version_parts(base_name):
            raise ValueError('ApplicationDisplayVersion debe ser numérica, por ejemplo 1.0.9.')
        if version_parts(published_name) and version_parts(published_name) > version_parts(base_name):
            base_name = published_name
        code = max(base_code, channel[0]) + 1
        if code > 2100000000:
            raise ValueError('No quedan códigos de versión Android disponibles.')
        name = f'{base_name}+{code}'
        with Path(args.output).open('a', encoding='utf-8') as output:
            output.write(f'code={code}\nname={name}\nchannel_digest={channel[1]}\n')
        print(f'Canal: {channel[0]}; versión preparada: {name} ({code}).')
    else:
        if args.code is None or not args.digest:
            parser.error('check requiere --code y --digest')
        ensure_can_publish(args.code, channel, args.digest)
        print(f'El canal sigue en {channel[0]}; la versión {args.code} puede publicarse.')


if __name__ == '__main__':
    main()
