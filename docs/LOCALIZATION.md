# Localización de VPS Desk

VPS Desk v2 incluye soporte de interfaz para inglés (`en-US`) y español (`es-ES`). La localización se implementa en la capa Desktop; dominio, Application e Infrastructure permanecen independientes del idioma.

## Selección de idioma

Al iniciar, VPS Desk resuelve el idioma en este orden:

1. Variable de entorno `VPSDESK_LANGUAGE`, si existe.
2. Idioma de interfaz del sistema operativo (`CurrentUICulture`).
3. Fallback a `en-US`.

Cualquier cultura que comience por `es` usa el paquete `es-ES`; cualquier cultura que comience por `en` usa `en-US`. Otros idiomas caen a inglés hasta que exista un paquete específico.

Ejemplos:

```powershell
$env:VPSDESK_LANGUAGE = 'es-ES'
./VpsDesk.Desktop.exe
```

```powershell
$env:VPSDESK_LANGUAGE = 'en-US'
./VpsDesk.Desktop.exe
```

La futura pantalla Settings deberá persistir esta preferencia y llamar a `LocalizationService.ApplyCulture(...)`, evitando depender permanentemente de una variable de entorno.

## Estructura

```text
src/VpsDesk.Desktop/
  Localization/
    LocalizationService.cs
    Languages/
      en-US/
        Strings.json
      es-ES/
        Strings.json
```

`en-US` es el paquete base/fallback. `es-ES` debe mantener exactamente las mismas claves. Si una traducción seleccionada no contiene una clave, `LocalizationService` conserva el valor inglés cargado previamente.

Los JSON se copian tanto al output de compilación como al publish final mediante `VpsDesk.Desktop.csproj`.

## Uso desde Avalonia XAML

Los recursos cargados se publican en `Application.Resources`, por lo que las vistas usan `DynamicResource`:

```xml
<TextBlock Text="{DynamicResource Nav_Servers}" />
<Button Content="{DynamicResource Common_Refresh}" />
```

No se deben volver a introducir literales visibles para el usuario cuando exista una clave de localización adecuada.

## Uso desde C# / ViewModels

Para textos generados desde código se puede usar:

```csharp
LocalizationService.T("Nav_Dashboard")
```

o:

```csharp
LocalizationService.Current.Format("Some_Format_Key", value);
```

La migración de mensajes de estado dinámicos de todos los ViewModels se hará de forma gradual. La primera fase localiza navegación, títulos, acciones y textos estáticos de los módulos actuales; los mensajes técnicos devueltos por Linux/Docker/SSH se conservan sin traducir para no ocultar información útil de diagnóstico.

## Validación

`Scripts/Validate-Localization.ps1` comprueba en CI:

- que existan los dos paquetes;
- que ambos JSON sean válidos;
- que `es-ES` tenga las mismas claves que `en-US`;
- que no existan valores vacíos.

El workflow `Avalonia v2 CI` ejecuta esta validación antes de restore/build. Un PR que agregue una clave sólo en un idioma debe fallar.

## Cómo agregar una cadena

1. Agregar la clave y texto original a `en-US/Strings.json`.
2. Agregar la misma clave traducida a `es-ES/Strings.json`.
3. Sustituir el literal en XAML por `{DynamicResource Clave}` o usar `LocalizationService.T("Clave")` en C#.
4. Ejecutar `Scripts/Validate-Localization.ps1`.
5. Compilar y ejecutar el smoke test.

## Alcance actual

Localizado en esta fase:

- shell principal y navegación;
- encabezados por módulo;
- Servers;
- Dashboard;
- Containers;
- Deployments;
- Logs;
- Storage;
- Files;
- Security.

Pendiente:

- mensajes dinámicos de estado generados por los ViewModels;
- etiquetas de enums como `Development`, `Staging`, `Production`, `PrivateKey` y `Password` sin alterar sus valores internos;
- selector de idioma en Settings con persistencia local;
- Terminal y Settings cuando sus pantallas definitivas estén implementadas;
- pluralización/formato localizado avanzado si el producto lo necesita.

Los nombres propios y términos técnicos como Docker, Compose, SSH, SFTP, systemd, journalctl y Nginx no se traducen.
