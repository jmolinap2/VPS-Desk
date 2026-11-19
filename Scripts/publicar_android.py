"""Compila el APK ARM64 firmado con la dirección de actualización y lo publica en Netlify.

Uso:  python Scripts/publicar_android.py [--notas "texto"] [--sin-subir]
Antes de cada release hay que subir ApplicationVersion en VpsDesk.Android.csproj.
Variables de usuario (se leen del registro si no están en el entorno):
  VPSDESK_KEYSTORE_PASS  contraseña del keystore (ya configurada)
  NETLIFY_AUTH_TOKEN     token personal de Netlify (solo para subir)
"""
import argparse, hashlib, json, os, re, shutil, subprocess, sys, tempfile, winreg
from pathlib import Path
from android_release_version import ensure_can_publish, read_channel

SITE_ID = '9892281b-d354-4673-96b3-f86eba7c57f6'
SITE_URL = 'https://candid-concha-ffbd40.netlify.app'
KEYSTORE = r'C:\Users\jmolina\.vpsdesk-keys\vpsdesk-release.keystore'
JDK = r'C:\Android\jdk'
ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / 'src/VpsDesk.Android/VpsDesk.Android.csproj'
GLOBAL_JSON = ROOT / 'global.json'


def user_var(name):
    if os.environ.get(name):
        return os.environ[name]
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
            return winreg.QueryValueEx(key, name)[0]
    except FileNotFoundError:
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--notas', default='')
    parser.add_argument('--sin-subir', action='store_true')
    args = parser.parse_args()

    text = CSPROJ.read_text(encoding='utf-8')
    code = int(re.search(r'<ApplicationVersion>(\d+)<', text).group(1))
    name = re.search(r'<ApplicationDisplayVersion>([^<]+)<', text).group(1)

    password = user_var('VPSDESK_KEYSTORE_PASS')
    if not password:
        sys.exit('Falta VPSDESK_KEYSTORE_PASS.')
    token = None if args.sin_subir else user_var('NETLIFY_AUTH_TOKEN')
    if not args.sin_subir and not token:
        sys.exit('Falta NETLIFY_AUTH_TOKEN (token personal de Netlify). Usa --sin-subir para solo compilar.')

    channel = None
    if not args.sin_subir:
        try:
            channel = read_channel(f'{SITE_URL}/version.json')
            ensure_can_publish(code, channel)
        except Exception as exc:
            sys.exit(f'Publicación cancelada: {exc}')

    env = os.environ.copy()
    env.update(VPSDESK_KEYSTORE_PASS=password, VPSDESK_KEY_PASS=password,
               ANDROID_HOME=r'C:\Android\sdk', JAVA_HOME=JDK)
    build = ['dotnet', 'publish', str(CSPROJ), '-c', 'Release', '-m:1', '-nr:false',
             '-p:UseSharedCompilation=false', '-p:UsedAvaloniaProducts=', f'-p:JavaSdkDirectory={JDK}',
             '-p:AbiOnly=arm64', f'-p:VpsDeskUpdateUrl={SITE_URL}/version.json',
             '-p:AndroidKeyStore=true', f'-p:AndroidSigningKeyStore={KEYSTORE}', '-p:AndroidSigningKeyAlias=vpsdesk',
             '-p:AndroidSigningKeyPass=env:VPSDESK_KEY_PASS', '-p:AndroidSigningStorePass=env:VPSDESK_KEYSTORE_PASS',
             '-v:minimal']

    # el SDK por defecto (11) no trae el workload android
    had_global = GLOBAL_JSON.exists()
    if not had_global:
        GLOBAL_JSON.write_text('{"sdk":{"version":"10.0.403","rollForward":"latestFeature"}}')
    try:
        if subprocess.run(build, env=env, cwd=ROOT).returncode != 0:
            sys.exit('La compilación falló.')
    finally:
        if not had_global:
            GLOBAL_JSON.unlink(missing_ok=True)

    apk = ROOT / 'src/VpsDesk.Android/bin/Release/net10.0-android/publish/com.vpsdesk.app-Signed.apk'
    if not apk.exists():
        sys.exit('No se encontró el APK firmado.')

    site = Path(tempfile.mkdtemp(prefix='vpsdesk-site-'))
    apk_name = f'vpsdesk-{code}.apk'
    shutil.copy(apk, site / apk_name)
    sha = hashlib.sha256((site / apk_name).read_bytes()).hexdigest().upper()
    (site / 'version.json').write_text(json.dumps(
        {'versionCode': code, 'versionName': name, 'apkUrl': apk_name, 'sha256': sha, 'notes': args.notas}), encoding='utf-8')
    (site / '_headers').write_text('/version.json\n  Cache-Control: no-cache\n/*\n  X-Robots-Tag: noindex, nofollow\n')
    (site / 'robots.txt').write_text('User-agent: *\nDisallow: /\n')
    print(f'versionCode={code} versionName={name} apk={apk.stat().st_size / 1048576:.1f} MB sha256={sha}')
    print(f'APK: {apk}')

    if args.sin_subir:
        print(f'Sitio preparado (sin subir): {site}')
        return
    try:
        ensure_can_publish(code, read_channel(f'{SITE_URL}/version.json'), channel[1])
    except Exception as exc:
        sys.exit(f'Publicación cancelada: {exc}')
    env['NETLIFY_AUTH_TOKEN'] = token
    deploy = ['npx', '--yes', 'netlify-cli@27.17.0', 'deploy', '--prod', '--dir', str(site), '--site', SITE_ID, '--no-build']
    sys.exit(subprocess.run(deploy, env=env, shell=True).returncode)


if __name__ == '__main__':
    main()
