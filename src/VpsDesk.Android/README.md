# VpsDesk.Android

Cabecera Android de VPS Desk, construida con `Avalonia.Android`. Reutiliza
toda la UI y lógica de `VpsDesk.UI` (la misma que usa `VpsDesk.Desktop`); este
proyecto solo aporta lo específico de la plataforma: `AndroidManifest.xml`,
`MainActivity`, íconos de lanzador y el `.csproj` con el workload Android.

## Requisitos para compilar

```powershell
dotnet workload install android
```

(o `android-aot` según la versión del SDK). Sin el workload instalado,
`dotnet build src\VpsDesk.Android\VpsDesk.Android.csproj` falla con un error
de TargetFramework `net10.0-android` desconocido — eso es normal hasta
instalarlo.

## Qué falta después de este scaffolding

1. **Layout responsivo.** `MainShell.axaml` (en `VpsDesk.UI/Views`) sigue
   siendo el layout de escritorio: sidebar fija de 250px + panel de
   contenido. Funciona al compilar y arrancar, pero en un teléfono en
   vertical el sidebar ocupa ~2/3 del ancho. El siguiente paso es un host
   específico para pantallas angostas (por ejemplo un `Flyout`/`SplitView`
   que colapse la navegación a un menú hamburguesa bajo cierto `Width`).
2. **Almacenamiento local.** `AppSettingsStore`, `ServerProfileStore`,
   `SqliteOperationHistoryStore`, etc. (en `VpsDesk.UI/Services`) usan rutas
   de archivo pensadas para escritorio (`Environment.GetFolderPath` /
   `%AppData%`). En Android hay que verificar que resuelvan a un directorio
   válido de la sandbox de la app (`Context.FilesDir` vía
   `Android.App.Application.Context`), o inyectar la ruta desde
   `MainActivity`.
3. **Terminal interactiva.** `SshNetInteractiveTerminalService` y la vista de
   terminal no se probaron en Android; confirmar que el control de texto se
   comporte bien con el teclado en pantalla.
4. **Ícono de lanzador.** Los PNG en `Resources/mipmap-*/ic_launcher.png` se
   generaron automáticamente a partir de `VpsDesk.UI/Assets/vps-desk.ico`
   (reescalado simple) — conviene reemplazarlos por un ícono diseñado para
   las resoluciones de Android (idealmente un adaptive icon).
5. **Firma / distribución.** No hay configuración de firma de APK todavía;
   necesaria antes de instalar fuera de depuración o publicar.
