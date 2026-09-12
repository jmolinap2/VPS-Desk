# VPS Desk

VPS Desk es un panel de control de escritorio para Windows pensado para monitorear y operar servidores VPS Linux mediante SSH. Está construido con PowerShell 7 y WPF, así que permite darle a un VPS una interfaz gráfica práctica sin instalar un entorno de escritorio remoto dentro del servidor.

## Funciones

- Dashboard de escritorio para disponibilidad del VPS, CPU, memoria, disco y uptime.
- Verificaciones por SSH usando llave privada o contraseña.
- Visibilidad de servicios Docker comunes como `sql`, `api` y `front`.
- Vista de almacenamiento con uso de disco, imágenes Docker y directorios Docker más pesados.
- Visor de logs remotos para contenedores Docker y journals del sistema.
- Lanzador de deploys y migraciones sobre scripts PowerShell existentes.
- Selector de entorno para Development, Staging y Production.
- Guardas de producción para opciones riesgosas de deploy.
- Enmascaramiento local de contraseñas, tokens, secretos y valores largos codificados.

## Requisitos

- Windows 10 o Windows 11.
- PowerShell 7 o superior, disponible como `pwsh`.
- Cliente OpenSSH disponible en el `PATH`.
- Acceso SSH al VPS objetivo.
- Docker en el VPS para funciones relacionadas con contenedores, storage y logs.
- Opcional: un token de Git solo si tu flujo de deploy necesita acceder a repositorios privados.

## Inicio Rápido

1. Clona o descarga este repositorio.
2. Copia `.env.example` como `.env`.
3. Edita `.env` con tu host VPS, usuario SSH y rutas locales.
4. Ejecuta `Run.bat`.

También puedes iniciarlo directamente desde PowerShell:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\VpsDesk.ps1
```

## Configuración

VPS Desk lee la configuración local desde un archivo `.env` en la carpeta del proyecto. Usa `.env.example` como plantilla pública.

Variables importantes:

- `SERVER_HOST`: IP o DNS del VPS.
- `SERVER_USER`: usuario SSH.
- `SSH_PORT`: puerto SSH, normalmente `22`.
- `SSH_KEY_PATH`: ruta a tu llave privada SSH.
- `REPO_LOCAL`: ruta local del proyecto que contiene tus scripts de deploy.
- `REMOTE_REPO_PATH`: ruta remota de tu aplicación dentro del VPS.
- `COMPOSE_FILE`: nombre del archivo Docker Compose en el VPS.
- `GIT_TOKEN`: token opcional para repositorios privados.
- `SSH_PASSWORD`: contraseña opcional para autenticación SSH por password.

Nunca subas `.env`, tokens reales, llaves privadas, contraseñas o logs generados.

## Notas De Uso

- La página Dashboard puede verificar conectividad y obtener métricas mediante SSH.
- La página Storage puede inspeccionar uso de disco Docker y ejecutar comandos de limpieza. Revisa las acciones destructivas antes de confirmarlas.
- Log Center puede cargar logs desde contenedores Docker o `journalctl`.
- Operations espera encontrar scripts de deploy o migración dentro de la carpeta `scripts` de `REPO_LOCAL`.

## Estructura Del Proyecto

```text
.
|-- Assets/                 # Recursos de la aplicación
|-- docs/                   # Roadmap de producto y documentos de planificación
|-- Schemas/                # Layout WPF en XAML
|-- Scripts/
|   |-- Core/               # Estado, carga de env, health checks, seguridad de logs
|   `-- GUI/                # Ventana, navegación y handlers de UI
|-- Run.bat                 # Launcher para Windows
|-- VpsDesk.ps1             # Punto de entrada principal en PowerShell
|-- .env.example            # Plantilla pública de configuración
|-- LICENSE                 # Licencia MIT
`-- README.md
```

## Roadmap

Consulta [docs/PRODUCT_ROADMAP.md](docs/PRODUCT_ROADMAP.md) para el plan de producto por fases.

## Seguridad

Esta herramienta puede ejecutar comandos contra un servidor real. Usa un usuario SSH con los mínimos privilegios posibles, protege tus llaves privadas y prueba las acciones en Development o Staging antes de Production.

Si un token o contraseña fue subido, compartido en un issue o incluido en un zip público, rótalo inmediatamente.

## Contribuir

Las contribuciones son bienvenidas. Mantén los cambios pequeños, evita subir configuración específica de tu máquina y documenta cualquier comportamiento que pueda modificar un VPS remoto.

## Licencia

MIT. Consulta `LICENSE`.
