# Despliegue de VPS Desk en el android

Script: `scripts\android-deploy.ps1` (o doble clic en `scripts\android-deploy.cmd`).
Sin parámetros abre un menú. Con parámetros ejecuta solo lo indicado.

## Requisitos (una sola vez)

1. **Depuración inalámbrica** activa en el teléfono y el PC en la misma red Wi-Fi.
2. **Carga de trabajo Android de .NET** para compilar desde la terminal:
   `dotnet workload install android` (puede pedir ejecutar la terminal como administrador).
   Si no quieres instalarla, compila desde Visual Studio y usa `-Apk`. El proyecto Android **no está**
   en `VpsDesk.slnx` (la CI no puede compilarlo sin la carga `android`). Abre en su lugar
   `src\VpsDesk.Android\VpsDesk.Android.slnx` (Archivo > Abrir > Proyecto o solución), elige
   *Release* y compila `VpsDesk.Android`. El APK queda en
   `src\VpsDesk.Android\bin\Release\net10.0-android\com.vpsdesk.app-Signed.apk`.
3. **Secretos fuera del repositorio.** El `.env` de la raíz del repo está ignorado por git
   (`.gitignore`). No lo subas ni lo copies a otros repos. La clave SSH se toma de `SSH_KEY_PATH`
   del `.env`. Solo si esa ruta no es válida, indica la clave con `-KeySource`.

## Rutina normal (cada cambio)

```
scripts\android-deploy.cmd -All
```

Hace, en orden:

1. Compila el APK en *Release*.
2. Detecta el teléfono por identidad (modelo `2311DRK48G`, dispositivo `duchamp`), sin puertos fijos.
3. Instala con `adb install -r`, que conserva los datos de la app.
4. Copia `.env` y la clave a `/sdcard/Android/data/com.vpsdesk.app/files/`, y verifica el SHA-256.
5. Abre la app.

## Opciones

| Parámetro | Qué hace |
|---|---|
| `-Doctor` | Diagnóstico de solo lectura: teléfono, app, archivos y APK disponible. |
| `-Pair` | Emparejamiento; solo si Android pide un nuevo emparejamiento. |
| `-Build` | Compila el APK en *Release*. |
| `-Install` | Instala el APK más reciente (o el indicado con `-Apk`). |
| `-Secrets` | Copia `.env` y la clave SSH. Usa `-SecretsEnv` y `-KeySource` si están en otra ruta. |
| `-Launch` | Abre la app en el teléfono. |
| `-Logs` | Muestra el registro reciente de la app. |
| `-All` | Compila, instala, copia secretos y abre la app. |
| `-DryRun` | Muestra lo que haría sin compilar, instalar ni copiar. |
| `-AdbPath` | Ruta de `adb.exe` si no está en `C:\Android\Sdk\platform-tools`. |

Ejemplos:

```
scripts\android-deploy.ps1 -Doctor
scripts\android-deploy.ps1 -Install -Apk "C:\ruta\com.vpsdesk.app-Signed.apk"
scripts\android-deploy.ps1 -Secrets -KeySource "C:\ruta\privada\holoscoreops_vps_ed25519" -DryRun
```

## Qué hay en el teléfono

| Ruta en el teléfono | Contenido |
|---|---|
| `/sdcard/Android/data/com.vpsdesk.app/files/.env` | Configuración y secretos del servidor (la app la lee al arrancar). |
| `/sdcard/Android/data/com.vpsdesk.app/files/<clave>` | Clave SSH privada, con el nombre que indica `SSH_KEY_PATH`. |

La app busca el `.env` en esa carpeta (`MainApplication.cs`). El script reescribe `SSH_KEY_PATH`
con el nombre de archivo, así la clave se encuentra junto al `.env` aunque en el PC esté en otra ruta.

## Problemas frecuentes

- **El teléfono no aparece:** abre "Depuración inalámbrica" y ejecuta `-Pair` si Android pide un nuevo emparejamiento.
- **`protocol fault` al emparejar:** el código expiró. Vuelve a abrir "Vincular dispositivo con un código de emparejamiento" y repite.
- **`10013` / `WSAEACCES`:** el script reinicia el servidor ADB y reintenta solo.
- **`UPDATE_INCOMPATIBLE`:** la firma del APK no coincide. El script no desinstala. Desinstalar borra los datos de la app y los secretos copiados: después de hacerlo, repite `-Install` y `-Secrets`.
- **MIUI bloquea la instalación:** activa "Instalar vía USB" y "Depuración de seguridad" en Opciones de desarrollador.
- **El APK es más viejo que tu código:** el script avisa; compila de nuevo con `-Build`.

## Seguridad

- El script nunca imprime valores del `.env`: solo nombres de variables y resultados de verificación.
- El `.env` temporal que se genera para el teléfono se borra al terminar, incluso si falla.
- El script nunca hace `git add` ni commit. Los secretos no entran al repositorio.
