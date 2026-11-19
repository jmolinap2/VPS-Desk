# Actualizaciones Android

El flujo `.github/workflows/android-release.yml` se ejecuta en cada **push a
`main`** y también admite ejecución manual desde GitHub Actions. Un commit que
solo existe en el equipo, un push a otra rama o un pull request no publican el
APK. El primer push que incluya el workflow activa la automatización.

El workflow consulta el `version.json` del canal y prepara un código superior
al publicado y al código base del proyecto. El nombre visible incluye ese código
como sufijo, por ejemplo `1.0.9+11`. Compila y firma el APK ARM64 con la URL del
canal, comprueba el paquete y publica el APK y su manifiesto en Netlify. Después
descarga el APK publicado y verifica su SHA-256. Si el canal cambió durante la
compilación o `main` ya tiene otro commit, cancela esa publicación. Los pushes
sucesivos cancelan el workflow anterior para priorizar el más reciente.

Los secretos de GitHub Actions requeridos son `ANDROID_KEYSTORE_BASE64`,
`ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` (opcional),
`ANDROID_UPDATE_URL`, `NETLIFY_AUTH_TOKEN` y `NETLIFY_SITE_ID`. El workflow
falla si faltan los necesarios. No se sube el APK como artifact público.

La compilación automática no prueba el APK en un teléfono. Si falla cualquier
comprobación anterior a la publicación, no se publica. La comprobación posterior
puede detectar un problema después de que Netlify haya activado el despliegue;
en ese caso hay que revisarlo en Actions.

En el teléfono, VPS Desk consulta el canal al abrirse o desde Ajustes. Android
requiere que la persona confirme la instalación. El APK debe conservar la misma
clave de firma para actualizar sin desinstalar ni borrar datos.

`Scripts/publicar_android.py` continúa disponible para publicar manualmente.
Antes de subir, verifica que el código del proyecto sea mayor que el publicado y
que el manifiesto no haya cambiado durante la compilación. `--sin-subir` solo
prepara el paquete y no modifica el canal.
