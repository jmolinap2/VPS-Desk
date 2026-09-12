# VPS Desk v2 — Estado de implementación y trabajo pendiente

> Rama de trabajo: `rewrite/avalonia-v2`  
> Documento de estado: septiembre de 2026  
> Objetivo: dejar una fotografía clara de lo que ya fue reconstruido, lo que se reutilizó del antiguo migrador y lo que todavía falta antes de considerar la v2 lista para uso real.

---

## 1. Objetivo actual del producto

VPS Desk v2 ya no se plantea como un migrador exclusivo para Holos ni como una herramienta acoplada a Hostinger.

La dirección actual es:

> Cliente de escritorio agentless para administrar VPS Linux mediante SSH/SFTP, sin instalar un panel administrativo propio ni un agente permanente dentro del servidor.

El diseño se basa en capacidades reales del servidor y no en el proveedor. Hostinger es el primer entorno probado/certificado, pero el núcleo debe poder trabajar con cualquier VPS Linux que cumpla los requisitos detectados por la aplicación.

Modelo conceptual:

```text
PC del operador
    |
    | VPS Desk
    |
    | SSH / SFTP
    v
VPS Linux
    |- OpenSSH
    |- systemd
    |- Docker / Docker Compose
    |- Git
    |- Nginx u otros servicios
    |- Aplicaciones y bases de datos
```

No se necesita instalar VPS Desk dentro del VPS.

---

## 2. Arquitectura implementada

La reconstrucción usa una arquitectura modular de escritorio, manteniendo separadas presentación, casos de uso, dominio e infraestructura.

```text
VpsDesk.Desktop
    Avalonia
    MVVM
    LiveCharts2
    Views / ViewModels
        |
        v
VpsDesk.Application
    Casos de uso
    Abstracciones
    Preflight
    Logging
        |
        v
VpsDesk.Domain
    ServerProfile
    Capabilities
    Metrics
    Containers
    Storage
    Operaciones
        ^
        |
VpsDesk.Infrastructure
    SSH.NET
    SFTP
    Linux probes
    Docker
    systemd / journalctl
    Storage
```

Principios ya aplicados:

- el dominio no depende de Hostinger;
- la comunicación remota se abstrae detrás de servicios;
- la UI no debería construir comandos administrativos directamente;
- las capacidades se detectan en el VPS antes de habilitar determinadas funciones;
- los secretos no forman parte del modelo persistido del servidor;
- no se expone la Docker API por red para poder administrar contenedores;
- no se instala un agente propio en el servidor;
- las operaciones peligrosas deben exigir confirmación explícita;
- Production se considera un entorno de mayor riesgo.

---

## 3. Shell e interfaz general

### Implementado

La interfaz WinForms original fue reemplazada por una nueva aplicación Avalonia.

El shell actual ya incluye:

- sidebar permanente;
- servidor activo visible;
- estado SSH visible;
- etiqueta de compatibilidad;
- encabezado contextual por módulo;
- navegación a módulos principales;
- tema oscuro moderno;
- layout pensado para 1440×900 con tamaño mínimo controlado;
- componentes separados por pantalla en lugar de concentrar toda la UI dentro de un único archivo gigante.

Navegación definida:

1. Servers
2. Dashboard
3. Containers
4. Deployments
5. Logs
6. Storage
7. Files
8. Terminal
9. Security
10. Settings

Actualmente Terminal y Settings siguen pendientes de implementación completa.

---

## 4. Servers — gestión de VPS

### Implementado

Existe gestión local de perfiles de servidores.

Cada perfil puede manejar:

- nombre;
- host/IP;
- puerto SSH;
- usuario;
- proveedor informativo;
- ambiente Development / Staging / Production;
- autenticación por llave privada;
- autenticación por contraseña;
- ruta de llave privada;
- huella SHA256 opcional de la clave SSH del servidor.

También están implementadas las operaciones:

- crear perfil;
- editar perfil;
- guardar perfil;
- eliminar perfil;
- probar conexión;
- seleccionar servidor activo;
- ejecutar Compatibility Check.

### Seguridad ya aplicada

Las contraseñas y passphrases no se guardan en el archivo local de perfiles. Actualmente permanecen sólo durante la sesión.

Cuando se configura `SSH host fingerprint SHA256`, VPS Desk puede rechazar un host cuya clave SSH no coincida con la esperada.

### Pendiente

- Credential Manager / Keychain / secret store real por sistema operativo;
- importación/exportación segura de perfiles;
- etiquetas/grupos de servidores;
- mejor UX para múltiples VPS;
- selección visual de llave mediante file picker;
- flujo TOFU controlado para primera conexión, sin aceptar claves silenciosamente;
- historial de última conexión y último error por servidor.

---

## 5. Compatibility Check y discovery del servidor

### Implementado

La aplicación ya detecta capacidades reales del VPS, entre ellas:

- SSH;
- Linux;
- Bash;
- systemd;
- journalctl;
- Docker;
- Docker Compose;
- Git;
- Nginx.

La compatibilidad deja de depender del proveedor y pasa a depender de lo que el servidor realmente soporte.

Clasificación conceptual actual:

- Certified;
- Compatible;
- Partial;
- Unsupported;
- Unknown.

Hostinger puede marcarse como certificado únicamente en las combinaciones realmente probadas.

### Pendiente

- persistir la matriz detectada por servidor;
- registrar versión de distribución;
- registrar versión de Docker y Compose;
- distinguir compatibilidad esperada de compatibilidad certificada;
- pruebas reales con Debian y otras distribuciones;
- pruebas reales con proveedores distintos de Hostinger;
- página detallada de compatibilidad/capabilities.

---

## 6. Dashboard y telemetría

### Implementado

Se obtiene telemetría real mediante SSH.

Métricas incluidas:

- CPU;
- memoria RAM;
- swap;
- disco;
- red RX/TX;
- load average 1/5/15;
- uptime.

La UI muestra:

- gauge de CPU;
- porcentaje de memoria;
- gauge de disco;
- uptime;
- información de red;
- gráfica temporal CPU/RAM con LiveCharts2;
- estado de Docker;
- estado de Nginx;
- última actualización.

Existe refresco automático después de una conexión válida.

### Pendiente

- configuración de frecuencia desde Settings;
- pausar polling cuando la app esté minimizada/inactiva;
- historial persistente opcional;
- alertas locales por umbrales;
- mejores estados degraded/warning;
- actividad reciente real en lugar de placeholder;
- acceso rápido desde Dashboard a contenedores/servicios con problemas.

---

## 7. Containers / Docker

### Implementado

Existe inventario real de contenedores Docker a través de SSH.

Se usan formatos estructurados de Docker, evitando parsear salidas humanas cuando existe JSON disponible.

Información obtenida:

- ID;
- nombre;
- imagen;
- estado;
- status;
- puertos;
- proyecto Docker Compose;
- health;
- CPU;
- memoria;
- porcentaje de memoria;
- network I/O.

La pantalla incluye resumen de:

- total;
- running;
- stopped;
- unhealthy;
- proyectos Compose.

Acciones implementadas:

- Start;
- Stop;
- Restart.

Las acciones requieren confirmación explícita.

Los identificadores de contenedor son validados antes de incorporarlos a comandos remotos para reducir riesgos de command injection.

### Deliberadamente no expuesto todavía

- delete container;
- prune images;
- prune system;
- remove volume;
- operaciones destructivas masivas.

### Pendiente

- vista por proyecto Compose;
- logs rápidos desde el detalle del contenedor;
- inspect estructurado;
- variables/labels seleccionadas con ocultación de secretos;
- recreate/pull controlado;
- health history;
- restart policy;
- ordenamiento y filtros;
- búsqueda;
- actualización parcial de métricas sin recargar todo el inventario.

---

## 8. Logs

### Implementado

Existe un Log Center real para consultar información directamente desde el VPS.

Fuentes actuales:

- Docker logs;
- systemd / journalctl.

Funciones implementadas:

- elegir tipo de fuente;
- elegir contenedor o servicio;
- listar servicios systemd;
- seleccionar cantidad de líneas;
- cargar logs;
- filtrar texto cargado;
- limpiar vista;
- salida monoespaciada.

Se corrigió el problema conceptual del migrador original donde todo lo enviado por `stderr` terminaba tratado automáticamente como error. En la nueva arquitectura stdout/stderr y severidad lógica no deben considerarse equivalentes.

También se reutilizó y adaptó lógica de:

- sanitización de secretos;
- clasificación de logs.

La sanitización contempla patrones como:

- password/pwd;
- token;
- API key;
- secret;
- GitHub tokens;
- Bearer tokens;
- Password dentro de connection strings;
- User Id dentro de connection strings;
- argumentos heredados `-SshPassword` y `-GitToken`.

### Pendiente

- streaming/follow (`docker logs -f`, `journalctl -f`) con cancelación robusta;
- filtros por severidad normalizada;
- exportación segura;
- timestamps normalizados;
- colores por severidad;
- búsqueda incremental eficiente;
- integración con historial de operaciones de VPS Desk;
- correlación entre deployment y logs.

---

## 9. Storage

### Implementado

Existe un módulo Storage read-only.

Incluye información de:

- filesystem raíz;
- espacio disponible/usado;
- `docker system df`;
- imágenes Docker;
- volúmenes Docker.

La decisión actual es mantener Storage como módulo de observación antes de habilitar limpiezas destructivas.

### Pendiente

- directorios pesados;
- análisis de crecimiento;
- detalle por volumen;
- vínculo volumen ↔ contenedor;
- limpieza guiada y con simulación previa;
- política de retención;
- protección especial de volúmenes que puedan contener bases de datos.

---

## 10. Files / SFTP

### Implementado

Existe infraestructura SFTP real separada de la ejecución de comandos shell.

Ya se implementó soporte para:

- navegar directorios remotos;
- leer archivos de texto;
- escribir archivos de texto;
- editar archivos remotos;
- limitar el editor a archivos de texto razonables;
- confirmación antes de guardar cambios.

El servicio SFTP aplica la misma política de validación de host SSH que la ejecución de comandos.

### Pendiente

- upload/download binario desde UI;
- drag & drop;
- renombrar;
- copiar/mover;
- crear archivo/carpeta;
- borrar con confirmación reforzada;
- permisos/chmod/chown;
- diff antes de sobrescribir;
- backup automático del archivo antes de modificarlo;
- edición con elevación (`sudo`) de archivos protegidos sin abrir sesiones root generales.

---

## 11. Deployments

### Implementado

La antigua idea de "migrador Holos" fue convertida en un flujo genérico de deployment.

Se creó `DeploymentPreflightService` para validar antes de ejecutar un despliegue.

El preflight comprueba actualmente:

- conexión SSH;
- Git instalado;
- Docker instalado;
- Docker Compose disponible;
- repositorio remoto existente;
- rama requerida;
- compose file requerido;
- `.env` cuando corresponde.

Se endureció el quoting de parámetros shell para evitar que rutas, ramas o nombres controlados por configuración se conviertan fácilmente en command injection.

El flujo reconstruido contempla:

```text
Preflight
   ↓
git fetch
   ↓
checkout de rama
   ↓
git pull --ff-only
   ↓
docker compose pull
   ↓
docker compose up -d --build
   ↓
docker compose ps
```

La salida se sanitiza antes de mostrarse/registrarse.

Production recibe tratamiento visual de riesgo y debe requerir confirmaciones reforzadas.

### Reutilizado del Holos Migrator

Se conserva conceptualmente lo valioso del migrador original:

- ejecución remota;
- flujo de despliegue;
- preflight;
- trazabilidad;
- manejo de salida;
- idea de scripts operativos;
- sanitización;
- controles antes de ejecutar.

Se eliminan del núcleo:

- nombres Holos;
- rutas Holos hardcodeadas;
- compose files específicos de Holos;
- supuestos exclusivos de Hostinger;
- credenciales embebidas en logs;
- dependencia conceptual de un único proyecto.

### Pendiente

- UI final de deployment completamente validada contra servidor real;
- perfiles de deployment reutilizables;
- múltiples proyectos por servidor;
- historial persistente de deployments;
- duración y resultado;
- rollback estructurado;
- selección de commit/tag;
- preview de cambios;
- estrategia de migraciones de base de datos configurable;
- deployment sin Git mediante upload de artefactos;
- health check posterior;
- rollback automático opcional si falla health check;
- exclusión/backup de `.env` y secretos;
- bloqueo de ejecución concurrente sobre el mismo proyecto.

---

## 12. Security

### Implementado

Existe un módulo Security de auditoría read-only.

Revisa actualmente aspectos como:

- método de autenticación SSH;
- uso de fingerprint/pinning;
- uso de usuario root;
- configuración efectiva de sshd;
- puertos públicos;
- posible exposición de PostgreSQL;
- SQL Server;
- MySQL;
- Redis;
- MongoDB;
- Docker API;
- UFW;
- firewalld;
- fail2ban.

La primera etapa es intencionalmente diagnóstica. VPS Desk todavía no debe aplicar hardening automático de manera silenciosa.

### Pendiente

- score de riesgo;
- explicación y recomendación por hallazgo;
- remediaciones asistidas;
- preview exacto de cambios;
- backup antes de modificar `sshd_config`, firewall o reverse proxy;
- verificación de que una modificación SSH no deje al usuario fuera del servidor;
- políticas específicas por ambiente;
- auditoría periódica opcional;
- validación TLS/reverse proxy;
- revisión de permisos de archivos sensibles;
- revisión de Docker socket;
- revisión de contenedores privilegiados/capabilities peligrosas;
- secretos en variables Docker/Compose.

---

## 13. Terminal

### Estado

Pendiente.

La navegación ya está reservada, pero no debe considerarse funcional.

### Alcance previsto

- sesión SSH interactiva;
- terminal real, no simulación mediante TextBox + comandos independientes;
- resize PTY;
- copy/paste;
- cancelación;
- reconexión;
- indicador claro del servidor/ambiente;
- Production destacado;
- evitar almacenar el historial de comandos sensibles por defecto.

Se debe evaluar cuidadosamente el control de terminal compatible con Avalonia antes de implementar esta pantalla.

---

## 14. Settings

### Estado

Pendiente.

### Debe incluir

- tema Light/Dark/System;
- intervalo de telemetría;
- timeout SSH;
- política de auto-refresh;
- directorio local de datos;
- política de logs;
- retención de historial;
- comportamiento de confirmaciones;
- gestión de secretos locales;
- configuración de terminal;
- defaults de deployment;
- idioma en una fase posterior.

No debe convertirse en una pantalla llena de opciones técnicas innecesarias para el MVP.

---

## 15. Servicios / systemd

### Estado

Existe infraestructura indirecta mediante detection y logs, pero todavía no hay una pantalla Services completa.

### Pendiente

Debe incorporarse cuando se priorice:

- listado de units relevantes;
- active/inactive/failed;
- start;
- stop;
- restart;
- enable/disable;
- logs vinculados;
- uptime del servicio;
- filtro;
- confirmaciones antes de acciones en Production.

No es obligatorio que Services sea una pantalla principal del MVP si Containers cubre la mayoría del uso real inicial.

---

## 16. Seguridad local pendiente

Actualmente los secretos de sesión no se escriben en `servers.json`, lo cual es preferible a persistirlos en texto plano, pero todavía no es la solución final.

Debe implementarse:

### Windows

- Windows Credential Manager o DPAPI.

### Linux

- Secret Service / keyring compatible.

### macOS

- Keychain.

Nunca guardar:

- passwords;
- passphrases;
- tokens;
- private keys;
- connection strings completas;

en JSON plano o logs.

---

## 17. CI/CD del propio VPS Desk

### Implementado

Existe workflow de GitHub Actions para la v2.

Actualmente realiza:

1. checkout;
2. setup de .NET 10;
3. restore;
4. build Release;
5. smoke test de arranque de la aplicación desktop;
6. publish Windows x64;
7. upload del artefacto Windows x64.

En las iteraciones realizadas durante esta reconstrucción aparecieron errores reales de XAML/binding. Fueron detectados por CI, corregidos y las ejecuciones posteriores quedaron verdes.

### Importante

CI confirma:

- restauración;
- compilación;
- arranque básico;
- publicación.

CI NO confirma todavía que todas las operaciones SSH/Docker/SFTP funcionen correctamente contra el Hostinger real.

### Pendiente

- tests unitarios;
- tests de parsers;
- tests del quoting shell;
- tests de sanitización;
- tests de capability detection;
- mocks/fakes de SSH;
- tests de ViewModels;
- integration tests contra contenedores/VMs controlados;
- publicación Linux/macOS cuando sea necesario;
- release versionado.

---

## 18. Reutilización del antiguo Holos Migrator

La estrategia usada no es "tirar todo y empezar de cero".

### Reutilizado o adaptado

- conceptos de ejecución remota;
- preflight;
- despliegues;
- flujo operativo;
- clasificación de logs;
- sanitización;
- idea de dashboard/monitorización;
- SFTP/SSH como base operativa;
- conocimiento adquirido de Hostinger;
- validaciones previas antes de acciones críticas.

### Reemplazado

- WinForms como presentación principal;
- shell antiguo;
- branding Holos Migrator;
- navegación antigua;
- acoplamiento a un único sistema;
- tratamiento de `stderr == error`;
- valores específicos del proyecto en el núcleo.

### Conservado temporalmente

Los scripts/archivos legacy que todavía pueden contener comportamiento útil no deben eliminarse hasta alcanzar paridad funcional real.

La eliminación debe hacerse después de validar que la función equivalente existe y funciona en v2.

---

## 19. Lo que está listo a nivel de código vs. lo que está probado en producción

Es importante no confundir ambas cosas.

### Implementado y compilando

- arquitectura v2;
- shell Avalonia;
- profiles;
- SSH;
- host fingerprint pinning;
- discovery/capabilities;
- dashboard;
- telemetría;
- Containers;
- Logs;
- Storage;
- Files/SFTP;
- Deployment preflight;
- flujo base de Deployments;
- Security read-only;
- separación de Views;
- CI + publish Windows x64.

### Todavía requiere prueba real controlada

- comandos Docker contra el VPS real;
- logs de contenedores reales;
- journalctl real con permisos del usuario configurado;
- Files/SFTP en rutas reales;
- edición de archivos remotos;
- Storage con instalaciones Docker reales;
- Security audit con distintas configuraciones;
- deployment completo;
- comportamiento con sudo;
- escenarios de timeout/desconexión;
- host fingerprint en cambios/reinstalaciones de VPS;
- múltiples servidores.

No debe ejecutarse un deployment crítico de producción únicamente porque el CI esté verde.

---

## 20. Orden recomendado para continuar

### Fase inmediata — cerrar el núcleo operativo

- [x] Servers
- [x] Dashboard
- [x] Containers
- [x] Logs
- [x] Storage
- [x] Files base
- [x] Deployments base
- [x] Security read-only
- [ ] Terminal
- [ ] Settings
- [ ] persistencia segura de secretos

### Fase de validación real

- [ ] prueba Hostinger sin operaciones destructivas;
- [ ] comprobar fingerprints;
- [ ] comprobar métricas;
- [ ] comprobar contenedores;
- [ ] comprobar logs;
- [ ] comprobar Storage;
- [ ] comprobar SFTP;
- [ ] ejecutar preflight de deployment;
- [ ] deployment sobre entorno de prueba;
- [ ] verificar reconexión y errores de red;
- [ ] comprobar usuario no-root + sudo.

### Fase de madurez

- [ ] historial local de operaciones;
- [ ] SQLite si se requiere persistencia estructurada;
- [ ] perfiles de deployment;
- [ ] Services/systemd;
- [ ] alertas locales;
- [ ] backup/restore;
- [ ] health checks post-deploy;
- [ ] rollback;
- [ ] multi-servidor más avanzado;
- [ ] hardening asistido;
- [ ] mejor UX de compatibilidad.

### Fase posterior

- [ ] probar proveedores adicionales;
- [ ] marcar compatibilidad certificada únicamente después de pruebas reales;
- [ ] Linux/macOS como clientes si existe demanda;
- [ ] posible Hub opcional para monitoreo 24/7/equipos;
- [ ] integraciones específicas de proveedor sólo si aportan snapshots, power control, firewall, DNS u otras APIs reales.

---

## 21. Criterio para considerar MVP terminado

El MVP no debería considerarse terminado sólo por tener todas las pantallas visibles.

Debe poder cumplir de punta a punta al menos este escenario:

1. instalar/abrir VPS Desk en el equipo del operador;
2. registrar un VPS Linux;
3. verificar SSH y fingerprint;
4. detectar capacidades automáticamente;
5. ver CPU/RAM/disco/red/uptime;
6. listar contenedores;
7. reiniciar un contenedor con confirmación;
8. consultar logs;
9. navegar archivos mediante SFTP;
10. ejecutar un deployment Compose mediante preflight;
11. detectar configuraciones de seguridad riesgosas;
12. completar todo lo anterior sin instalar un agente de VPS Desk en el servidor.

Además debe demostrarse que:

- ningún secreto aparece en logs;
- los perfiles persistidos no contienen passwords/passphrases;
- un fingerprint incorrecto bloquea la conexión;
- un valor manipulable por usuario no puede convertirse trivialmente en shell injection;
- Production exige mayor cuidado que Development;
- una desconexión SSH no congela permanentemente la UI.

---

## 22. Definición actual del producto

VPS Desk no intenta ser cPanel, Plesk, Portainer o Coolify completos.

Su foco es:

> Operación visual y ligera de VPS Linux, Docker y despliegues desde una aplicación de escritorio mediante SSH/SFTP, manteniendo el servidor sin un panel administrativo adicional.

Ese enfoque debe protegerse para evitar que el proyecto termine convertido en una suite gigantesca antes de que el núcleo esté validado.

---

## 23. Resumen ejecutivo

La reconstrucción ya superó la etapa de simple rediseño visual.

Actualmente existe una nueva base técnica real con:

- Avalonia + MVVM;
- arquitectura separada por capas;
- soporte multi-servidor a nivel de modelo;
- SSH/SFTP;
- capability discovery;
- monitorización;
- Docker;
- logs;
- storage;
- archivos;
- deployments;
- auditoría de seguridad;
- sanitización;
- CI y publicación Windows x64.

Lo que falta ya no es reconstruir la idea principal, sino terminar los módulos incompletos, endurecer seguridad local, validar todos los flujos contra servidores reales y llevar las operaciones existentes desde "compila y está implementado" a "está probado y es confiable".

La prioridad inmediata recomendada es:

```text
Terminal
   +
Settings
   +
Secure Secret Storage
   +
Hostinger Real Validation
   +
Deployment Test Environment
```

Después de ese punto será razonable comenzar a hablar de una v2 MVP utilizable de forma cotidiana.
