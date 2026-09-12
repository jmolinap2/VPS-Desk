# VPS Desk

VPS Desk es un cliente de escritorio para administrar y observar VPS Linux mediante SSH/SFTP sin instalar un panel o agente propio dentro del servidor.

La nueva versión se está desarrollando en .NET 10 + Avalonia. Hostinger es el primer proveedor probado/certificado, pero el núcleo no depende de Hostinger: detecta las capacidades reales del VPS y puede trabajar con servidores Linux compatibles.

## Estado actual de v2

La rama `rewrite/avalonia-v2` contiene la reconstrucción moderna. Ya incluye:

- aplicación desktop Avalonia para Windows/Linux/macOS a nivel de framework;
- gestión local de perfiles de servidores;
- autenticación SSH por llave privada o contraseña;
- pinning opcional de huella SHA256 de la clave SSH del servidor;
- Compatibility Check de Linux, Bash, systemd, journalctl, Docker, Compose, Git y Nginx;
- telemetría real de CPU, RAM, swap, disco, red, load average y uptime;
- Dashboard con gauges y gráficas LiveCharts2;
- refresco automático de métricas después de una conexión válida;
- SFTP para lectura/escritura remota sin incrustar contenido sensible en comandos shell;
- preflight genérico de despliegue reutilizado y desacoplado de Holos;
- sanitización/clasificación de logs recuperada del Migrator;
- CI que compila, hace smoke test gráfico y genera un build Windows x64 de prueba.

Las páginas Containers, Deployments, Logs, Storage, Files, Terminal, Security y Settings todavía se están migrando. El shell ya reserva su navegación, pero no deben considerarse terminadas.

## Arquitectura v2

```text
VpsDesk.Desktop        Avalonia, MVVM, LiveCharts2
        |
VpsDesk.Application    Casos de uso y abstracciones
        |
VpsDesk.Domain         Perfiles, capacidades, métricas y operaciones
        ^
VpsDesk.Infrastructure SSH.NET, SFTP y probes Linux
```

El VPS no necesita instalar VPS Desk:

```text
PC del operador
  VPS Desk
     |
     | SSH / SFTP
     v
VPS Linux
  SSH + herramientas ya existentes
```

## Probar v2 desde código

Requisitos de desarrollo:

- .NET SDK 10.
- Windows, Linux o macOS compatible con Avalonia.
- acceso SSH a un VPS Linux.

```powershell
git switch rewrite/avalonia-v2
dotnet restore VpsDesk.slnx
dotnet run --project src/VpsDesk.Desktop/VpsDesk.Desktop.csproj
```

Al abrir la aplicación, entra a **Servers** para registrar un VPS. Los perfiles no sensibles se guardan en el perfil local del usuario. Las contraseñas y passphrases permanecen solo en memoria durante la sesión.

Para una primera conexión se recomienda verificar externamente la huella SSH del servidor y guardarla en el campo **SSH host fingerprint SHA256**. Cuando está configurada, VPS Desk rechaza una clave de host distinta.

## Bootstrap desde `.env`

La v2 puede importar una configuración inicial desde `.env` únicamente como mecanismo de transición/pruebas. Si no existen perfiles guardados, puede leer:

- `SERVER_NAME`
- `SERVER_PROVIDER`
- `SERVER_HOST`
- `SERVER_USER`
- `SSH_PORT`
- `SSH_KEY_PATH`
- `SSH_HOST_FINGERPRINT`
- `SSH_PASSWORD`

Los perfiles gestionados desde la UI sustituyen gradualmente esta dependencia. Las opciones de
despliegue no sensibles (ruta remota, rama, archivo Compose y nombre del archivo de entorno) se
guardan localmente por servidor después de detectarlas o al usar **Guardar perfil**. El `.env` no
se usa como almacenamiento principal de VPS Desk ni se copian sus secretos al perfil.
Como bootstrap, sus valores de despliegue se aplican únicamente al perfil que coincide en host,
puerto y usuario; una vez guardado un perfil local, este tiene prioridad.

## Código heredado

Los archivos PowerShell/WPF existentes se conservan temporalmente en la raíz (`VpsDesk.ps1`, `Scripts/`, `Schemas/`, `Run.bat`) para no perder capacidades mientras se realiza la migración. No forman parte de la arquitectura objetivo de v2 y se retirarán únicamente cuando exista paridad funcional comprobada.

`HolosMigratorUI` también se usa como fuente de capacidades probadas: se reutiliza la lógica generalizable, pero no se copian nombres, rutas, compose files ni supuestos específicos de Holos/Hostinger.

## Documentación

- `docs/V2_PLAN.md`: alcance general de la reconstrucción.
- `docs/ARCHITECTURE_V2.md`: arquitectura objetivo.
- `docs/UI_SPEC_V2.md`: especificación de interfaz.
- `docs/MIGRATION_FROM_HOLOS.md`: matriz de reutilización del Migrator.
- `docs/PRODUCT_ROADMAP.md`: roadmap funcional más amplio.

## Seguridad

VPS Desk puede ejecutar acciones administrativas reales. La versión final debe tratar como requisitos de primer nivel el pinning de host SSH, mínimo privilegio, secretos locales protegidos, sanitización de logs, confirmaciones reforzadas en Production y preflight antes de operaciones destructivas.

No subas `.env`, llaves privadas, contraseñas, tokens, backups ni logs sensibles al repositorio.

## Licencia

MIT. Consulta `LICENSE`.
