# Arquitectura VPS Desk v2

## Principio

La aplicación se diseña alrededor de capacidades del servidor, no alrededor del proveedor de hosting.

```text
VpsDesk.Desktop
       |
       v
VpsDesk.Application
       |
       v
VpsDesk.Domain
       ^
       |
VpsDesk.Infrastructure
```

## Proyectos

### VpsDesk.Domain

Sin dependencias de UI, SSH ni persistencia.

Responsabilidades:

- ServerProfile.
- ServerEnvironment.
- ServerCapability.
- CompatibilitySnapshot.
- ServerMetrics.
- ContainerSnapshot.
- OperationRun.
- LogEntry.
- AlertEvent.

Los modelos de métricas deben contener números y unidades claras, no texto destinado a UI.

### VpsDesk.Application

Casos de uso y contratos.

Contratos principales:

- ISshCommandExecutor.
- IServerProbeService.
- IContainerService.
- ILogService.
- IStorageService.
- IFileTransferService.
- IDeploymentService.
- IServerProfileRepository.
- ISecretVault.

Servicios reutilizables:

- LogSanitizer.
- LogClassifier.
- CompatibilityEvaluator.
- ProductionSafetyPolicy.

Application no conoce Avalonia, controles visuales ni SSH.NET.

### VpsDesk.Infrastructure

Implementaciones técnicas:

- SSH.NET para SSH/SFTP.
- LinuxServerProbeService.
- DockerCommandService.
- JournalLogService.
- LocalServerProfileRepository.
- WindowsDpapiSecretVault.

Toda ejecución SSH pasa por un único executor para centralizar timeout, cancelación, sanitización y trazabilidad.

### VpsDesk.Desktop

Avalonia + MVVM.

Responsabilidades:

- navegación;
- vistas;
- ViewModels;
- LiveCharts2;
- dialogos y confirmaciones;
- adaptación de métricas a series visuales;
- composition root.

Desktop no construye comandos SSH directamente.

## Modelo multi-servidor

ServerProfile representa conexión e identidad del VPS, no una aplicación desplegada.

Campos base:

- Id.
- Name.
- Host.
- Port.
- Username.
- AuthenticationType.
- PrivateKeyPath opcional.
- SecretReference opcional.
- ProviderLabel opcional.
- Environment.
- Tags.

DeploymentProfile representa una aplicación/proyecto sobre un servidor:

- Id.
- ServerId.
- Name.
- RemotePath.
- Branch.
- ComposeFiles.
- Services.
- HealthChecks.
- DeploymentStrategy.

Esto permite tener varios proyectos en un mismo VPS y evita acoplar la conexión SSH a Omni/Holos.

## Capability-driven design

Ejemplos de capacidades:

```text
Ssh
Linux
Bash
Systemd
Journalctl
Docker
DockerCompose
Git
Nginx
Sftp
```

La UI consulta CompatibilitySnapshot. Ejemplo:

- Docker ausente: Containers deshabilitado con explicación.
- systemd ausente: acciones de servicios systemd ocultas.
- journalctl ausente: Logs usa archivos conocidos si están disponibles.

ProviderLabel no debe decidir qué comandos ejecutar.

## SSH

El executor recibe:

- conexión;
- comando;
- timeout;
- CancellationToken.

Y retorna:

- ExitCode;
- StdOut;
- StdErr;
- Duration;
- Succeeded.

No debe devolver null para representar todos los fallos. Los errores deben ser distinguibles: timeout, auth, host unreachable, command error, cancelación.

## Métricas

ServerMetrics debe contener:

- CpuUsagePercent.
- Load1, Load5, Load15.
- MemoryUsedBytes / MemoryTotalBytes.
- SwapUsedBytes / SwapTotalBytes.
- DiskUsedBytes / DiskTotalBytes.
- NetworkRxBytesPerSecond / NetworkTxBytesPerSecond.
- Uptime.
- Timestamp.

CPU se obtiene de diferencias de /proc/stat entre dos muestras. Load average se presenta aparte y nunca se etiqueta como CPU %.

Para reducir round-trips SSH, cada ciclo de métricas debe agrupar consultas en uno o pocos comandos remotos.

## Estado y refresco

No usar un singleton mutable global equivalente a AppStateStore como núcleo de la aplicación.

Separar:

- estado observable de sesión en ViewModels;
- historial de operaciones mediante repositorio;
- métricas recientes mediante buffer acotado;
- alertas mediante servicio dedicado.

Los buffers deben tener límite de memoria.

## Logs

Pipeline:

```text
Raw output
   -> sanitización
   -> clasificación
   -> almacenamiento local opcional
   -> UI
```

Nunca invertir el orden: un secreto no debe entrar primero a un log persistente para luego ser ocultado visualmente.

## Deployments

El despliegue actual utiliza un pipeline nativo y genérico ejecutado por VPS Desk. El cliente PowerShell/WPF heredado y su ruta de ejecución fueron retirados de `main`; cualquier referencia histórica sigue disponible en Git.

El pipeline puede incluir:

- preflight;
- git pull/fetch;
- backup;
- docker compose pull/build;
- migraciones;
- docker compose up;
- health check;
- rollback manual/automatizable posteriormente.

Las recetas `.vpsdesk.yml` son opcionales y permiten que cada proyecto declare targets, servicios y migraciones sin introducir conocimiento específico del proyecto en el núcleo de VPS Desk.

## Persistencia

Primera etapa:

- perfiles no sensibles: JSON local versionado por esquema;
- secretos: ISecretVault;
- métricas: memoria.

Etapa posterior:

- SQLite local para histórico de métricas, deploys y eventos.

No usar .env como almacenamiento principal de la aplicación v2. Puede existir un importador de .env por compatibilidad.

## Proveedores

Hostinger será el primer proveedor certificado porque existe prueba real.

Estados sugeridos:

- Certified: probado manualmente por el proyecto.
- Compatible: pasó el Compatibility Check.
- Partial: faltan capacidades.
- Unsupported: no cumple requisitos mínimos.

El soporte futuro a APIs de proveedores debe implementarse como adaptadores opcionales.

## Dependencias técnicas iniciales

- Avalonia 12.1.2.
- Avalonia.Desktop 12.1.2.
- Avalonia.Themes.Fluent 12.1.2.
- Avalonia.Fonts.Inter 12.1.2.
- CommunityToolkit.Mvvm 8.4.2.
- LiveChartsCore.SkiaSharpView.Avalonia 2.0.5.
- SSH.NET 2026.0.0.

No agregar librerías solo por conveniencia si el BCL cubre la necesidad.
