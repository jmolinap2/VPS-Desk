# Auditoría de reutilización desde HolosMigratorUI

Este documento define qué se recupera, qué se adapta y qué se descarta al construir VPS Desk v2.

## Reutilización alta

### Core/LogSecurity.cs

Estado: portar y generalizar.

Valor existente:

- enmascara passwords;
- tokens;
- API keys;
- bearer tokens;
- credenciales de connection strings;
- argumentos SSH/Git.

Cambios:

- renombrar a LogSanitizer;
- agregar tests;
- evitar prefijos específicos de Holos;
- sanitizar antes de persistir cualquier salida.

### Core/LogClassifier.cs

Estado: portar.

Valor existente:

- clasificación Info/Warning/Error/Success/Debug;
- trata eventos normales de Docker enviados por stderr sin convertirlos automáticamente en errores.

Cambios:

- reglas configurables por fuente;
- tests unitarios;
- desacoplar español/strings específicos cuando sea necesario.

### Core/OperationsModels.cs

Estado: reutilizar conceptos, no copiar literalmente.

Se recuperan:

- LogEntry;
- OperationRunSummary;
- AlertEvent;
- DeploymentEnvironment;
- perfiles/presets de operación.

Cambios:

- modelos genéricos;
- ServerId y DeploymentProfileId;
- timestamps DateTimeOffset;
- métricas tipadas.

### Core/EnvironmentPolicy.cs

Estado: reutilizar concepto.

Se conserva:

- diferencias DEV/STAGING/PROD;
- guardrails de Production;
- validación de configuración insegura.

Cambios:

- convertir en ProductionSafetyPolicy;
- reglas extensibles por operación;
- no limitarse a SQL Server.

## Reutilización media

### Core/HealthCheckService.cs

Estado: extraer lógica y dividir responsabilidades.

Se recupera:

- prueba TCP del host;
- autenticación con SSH.NET;
- ejecución remota;
- comandos para free/df/uptime;
- consulta Docker;
- journalctl/syslog;
- consulta de almacenamiento.

Debe eliminarse:

- nombres holos-api, holos-front, holos-sql;
- ruta fija /root/OmniSuite/migrator_logs.txt;
- selección rígida de fuentes de log.

Debe corregirse:

- CPU no puede calcularse como load1 / cores. Eso mide carga, no utilización.
- retornar resultados tipados en vez de diccionarios string/string.
- separar SshCommandExecutor, LinuxProbeService, DockerService, LogService y StorageService.
- no ocultar todos los errores devolviendo null.

### MainShellForm.cs - ejecución de procesos

Estado: portar a un adaptador LegacyPowerShellDeploymentAdapter.

Se recupera:

- ProcessStartInfo;
- captura STDOUT/STDERR;
- cancelación del proceso;
- duración;
- exit code;
- sanitización antes de mostrar;
- progreso inferido desde output para scripts legacy.

Se elimina del shell visual y pasa a Infrastructure/Application.

### MainShellForm.cs - secretos

Estado: reutilizar solo implementación Windows.

Se recupera:

- DPAPI CurrentUser para proteger secretos locales.

Cambios:

- ISecretVault;
- WindowsDpapiSecretVault;
- otras plataformas no guardarán passwords hasta tener integración segura propia.

### DashboardUserControl.cs

Estado: reutilizar flujo funcional, rehacer UI.

Se recupera:

- refresco periódico;
- separación host reachable / datos remotos;
- estados operativo/degradado/offline;
- KPIs de operaciones;
- tabla de ejecuciones recientes.

Cambios:

- Avalonia MVVM;
- métricas numéricas;
- gráficos LiveCharts2;
- contenedores dinámicos, no tres servicios fijos.

### StorageUserControl.cs

Estado: reutilizar comandos/conceptos, rehacer parser y UI.

Se recupera:

- df;
- docker system df;
- docker images;
- top directories;
- prune de builder/images/system;
- confirmación para prune agresivo.

Cambios:

- servicios separados;
- resultados tipados;
- preview exacto de acción;
- no ejecutar system prune con volumes desde una acción primaria.

### UI/UIModules.cs - Log Center

Estado: reutilizar funcionalidad.

Se recupera:

- búsqueda;
- severidad;
- exportación;
- descarga de logs remotos;
- fuentes host/Docker.

Cambios:

- fuentes detectadas dinámicamente;
- sin nombres Holos;
- streaming opcional.

### UI/UIModules.cs - Settings/Environment

Estado: reutilizar concepto.

Se recupera:

- consulta de variables del host y contenedores.

Cambios:

- secretos siempre enmascarados;
- la pantalla Settings deja de ser una simple tabla de variables.

### AppStateStore.cs

Estado: no portar como singleton, sí reutilizar algoritmos.

Se recupera:

- buffers acotados;
- cálculo de success rate;
- duración promedio;
- idea de MTTR;
- evaluación de alertas.

Cambios:

- repositorios/servicios;
- estado por ServerId;
- persistencia opcional;
- deduplicación de alertas.

## No reutilizar como código

### WinForms Designer y controles

- MainShellForm.Designer.cs.
- DashboardUserControl.Designer.cs.
- StorageUserControl.Designer.cs.
- AnimatedRoundedButton.cs.
- UIHelper.cs.

Razón: Avalonia reemplaza completamente la capa visual. Los requisitos funcionales sí se conservan.

### Acoplamientos específicos

No deben pasar a v2:

- deploy-hostinger.ps1 como único flujo;
- migrate-hostinger-ui.ps1 como concepto central;
- docker-compose.hostinger.yml como default global;
- holos-api / holos-front / holos-sql;
- rutas OmniSuite;
- tenant/migration mode como campos universales del VPS.

Esos elementos pueden vivir dentro de un DeploymentProfile Legacy específico para conservar compatibilidad.

## VPS-Desk actual PowerShell/WPF

La implementación PowerShell/WPF actual se conserva en la rama mientras v2 alcanza paridad. No se usará como base técnica del nuevo Desktop; sirve como referencia funcional y fallback.

Estrategia:

```text
Legacy WPF/PowerShell + HolosMigratorUI
            |
            | extraer comportamiento probado
            v
Domain/Application/Infrastructure
            |
            v
Avalonia Desktop v2
```

Cuando v2 cubra Dashboard, SSH, Logs, Storage y Deploy legacy, el código anterior puede moverse a legacy/ o quedar congelado en una tag/release.

## Orden de migración

1. LogSanitizer y LogClassifier.
2. SSH executor.
3. ServerProfile y CompatibilitySnapshot.
4. métricas Linux correctas.
5. Docker genérico.
6. Dashboard Avalonia.
7. Logs y Storage.
8. LegacyPowerShellDeploymentAdapter.
9. Deploy pipeline nativo.
10. retirar acoplamientos Holos/Hostinger del flujo principal.
