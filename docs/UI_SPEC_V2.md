# Especificación de interfaz VPS Desk v2

## Dirección visual

Objetivo: una consola profesional de infraestructura, no una terminal decorada.

- Dark theme predeterminado.
- Light theme soportado.
- Tipografía Inter.
- Fondo oscuro neutro.
- Azul/cian como acento de interacción, no como color dominante en todo el contenido.
- Verde, amarillo y rojo reservados para estados semánticos.
- Animaciones cortas de 150 a 220 ms.
- Sin glows permanentes ni texto tipo hacker.
- Información densa pero legible.

## Shell

Tamaño recomendado inicial: 1440 x 900.
Tamaño mínimo: 1180 x 720.

### Sidebar

Ancho expandido: 224 px.
Ancho colapsado: 72 px.

Orden:

- logo/nombre VPS Desk;
- selector de servidor;
- Servers;
- Dashboard;
- Containers;
- Deployments;
- Logs;
- Storage;
- Files;
- Terminal;
- Security;
- separador;
- Settings.

El item activo usa un fondo discreto y barra/acento lateral. El sidebar no cambia de color según el módulo.

### Header

Alto: 64 px.

Izquierda:

- nombre de pantalla;
- breadcrumb opcional corto.

Derecha:

- servidor activo;
- badge DEV/STAGING/PROD;
- estado SSH;
- latencia;
- botón Refresh;
- campana de alertas.

## 1. Servers

Objetivo: administrar perfiles VPS.

Vista:

- barra superior con búsqueda, filtros por tag/estado y botón Add server;
- cards o tabla adaptable con nombre, host, proveedor, entorno, estado, OS y última conexión;
- acción Connect/Select;
- menú contextual Edit, Duplicate, Compatibility check, Remove.

Add/Edit Server se abre como wizard de 3 pasos:

1. Identity: nombre, host, puerto, usuario, provider label, entorno.
2. Authentication: llave o password, prueba de conexión.
3. Compatibility: detección y resumen.

Nunca mostrar contraseña guardada en texto plano.

## 2. Dashboard

Objetivo: saber en menos de 10 segundos si el VPS está sano.

### Franja de estado

- nombre del VPS;
- IP/host;
- Certified/Compatible/Partial;
- distro y kernel;
- uptime;
- última actualización.

### Fila de métricas

Cuatro cards:

CPU:
- gauge radial compacto;
- porcentaje actual;
- load 1/5/15 en texto secundario.

RAM:
- porcentaje grande;
- barra horizontal;
- usado / total.

Disk:
- donut;
- usado / total;
- libre.

Network:
- RX y TX actuales;
- sparkline de últimos minutos.

### Gráfico de rendimiento

Panel principal de aproximadamente 2/3 del ancho.

- LiveCharts2 line/area chart.
- rango: 5m, 15m, 1h.
- selector CPU/RAM/Network.
- máximo inicial: 300 puntos visibles por serie.

### Servicios

Panel lateral de 1/3:

- Docker daemon;
- contenedores destacados;
- Nginx;
- servicios configurados por el usuario.

Cada fila: estado, nombre, consumo resumido y acción contextual.

### Actividad reciente

- últimos deploys;
- reinicios;
- alertas;
- acciones destructivas.

## 3. Containers

Tabla principal:

- Name;
- Image;
- State;
- Health;
- CPU;
- RAM;
- Ports;
- Uptime.

Toolbar:

- Refresh;
- Compose project selector;
- Start;
- Restart;
- Logs;
- More.

Stop, Remove, Down y Prune no deben ser acciones primarias. Requieren confirmación con objeto afectado y comando.

Panel de detalle al seleccionar un contenedor:

- Overview;
- Environment, con secretos enmascarados;
- Mounts;
- Ports;
- Logs;
- Inspect JSON bajo demanda.

## 4. Deployments

Arriba:

- Deployment profile;
- branch;
- target;
- entorno;
- botón Deploy.

Antes de ejecutar se muestra Preflight:

- SSH;
- Git;
- Docker/Compose;
- espacio libre;
- branch;
- backup si aplica;
- health check actual.

Durante ejecución:

- stepper vertical;
- paso actual;
- tiempo por paso;
- output en vivo;
- botón Cancel.

Al final:

- éxito/fallo;
- duración;
- commit;
- health check;
- enlace a logs.

LegacyScript se mostrará como tipo de perfil mientras se migra desde HolosMigratorUI.

## 5. Logs

Layout dividido:

Izquierda:
- fuentes: Host, Docker, application profiles.

Centro:
- stream o snapshot de logs;
- timestamp;
- severity;
- source;
- message.

Arriba:
- búsqueda;
- severidad;
- rango temporal;
- Pause;
- Download;
- Export filtered.

Errores, warnings y éxitos usan énfasis semántico sin colorear filas completas de forma agresiva.

## 6. Storage

Resumen:

- disco usado/libre;
- Docker images;
- build cache;
- volumes;
- logs conocidos.

Visuales:

- donut del filesystem principal;
- barras horizontales para categorías;
- tabla de directorios pesados.

Acciones de prune separadas en Maintenance y siempre con preview.

## 7. Files

Explorador SFTP de dos paneles opcional:

- local;
- remoto.

Funciones MVP:

- navegar;
- subir;
- descargar;
- crear carpeta;
- renombrar;
- borrar con confirmación;
- editar archivos de texto pequeños en fase posterior.

La ruta remota debe ser siempre visible.

## 8. Terminal

Terminal SSH integrada con:

- pestañas por sesión;
- historial de comandos local opcional;
- botón Copy;
- reconexión;
- indicador claro de servidor y entorno.

Production debe mostrar una marca persistente y discreta para evitar confundir sesiones.

## 9. Security

Checklist de solo lectura por defecto:

- root login;
- password authentication;
- firewall;
- puertos expuestos;
- actualizaciones pendientes;
- Fail2ban si existe;
- expiración SSL para dominios configurados;
- presión de disco.

Cada hallazgo incluye estado, explicación breve y recomendación. Los botones de remediación se agregan después y nunca se ejecutan automáticamente.

## 10. Settings

Secciones:

- Appearance;
- Refresh intervals;
- Local storage;
- Secret storage;
- Logging;
- Advanced;
- About.

Import from legacy .env permitirá traer configuración existente sin convertir .env en dependencia permanente.

## Alert drawer

Panel desde la derecha, 380-440 px:

- Critical;
- Warning;
- Info;
- timestamp;
- server;
- acción sugerida.

No bloquear la navegación por alertas no críticas.

## Estados de carga y error

Cada módulo remoto debe distinguir:

- Loading;
- Offline;
- Authentication failed;
- Capability missing;
- Permission denied;
- Command failed;
- Empty result.

Evitar mensajes genéricos del tipo 'No se pudo'.

## Rendimiento visual

- No renderizar miles de puntos en los charts.
- Mantener buffer acotado.
- Actualizar propiedades existentes en vez de recrear toda la vista.
- No ejecutar comandos SSH desde el hilo de UI.
- Una actualización lenta de Storage no debe bloquear CPU/RAM.
