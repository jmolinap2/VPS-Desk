# VPS Desk v2 - Plan Maestro

## Objetivo

Reescribir VPS Desk como una aplicación de escritorio moderna, genérica y orientada a operación de VPS Linux por SSH, sin instalar un panel o agente propio en el servidor.

La primera compatibilidad certificada será Hostinger porque es el entorno probado actualmente. El diseño no dependerá de Hostinger: la compatibilidad se resolverá por capacidades detectadas del servidor.

## Decisiones base

- .NET 10.
- Avalonia UI 12.1.2.
- CommunityToolkit.Mvvm 8.4.2.
- LiveCharts2 2.0.5 para gráficas.
- SSH.NET 2026.0.0 para SSH/SFTP.
- Arquitectura por capas: Domain, Application, Infrastructure y Desktop.
- Sin agente obligatorio en el VPS.
- SSH como transporte operativo principal.
- Multi-VPS contemplado desde el modelo de datos, aunque el primer MVP puede enfocarse en un servidor seleccionado.
- Hostinger se mostrará como proveedor certificado/probado; otros VPS Linux se marcarán como compatibles según un chequeo de capacidades.

## Propuesta de valor

VPS Desk debe ser un cliente de escritorio para operar uno o varios VPS Linux sin instalar una consola web adicional en el servidor.

Flujo principal:

```text
PC del operador
    |
    | SSH / SFTP
    v
VPS Linux
    |- Docker / Compose
    |- systemd
    |- Nginx
    |- aplicaciones
    `- bases de datos
```

Al cerrar VPS Desk no queda un servicio VPS Desk ejecutándose en el servidor.

## Alcance de interfaz

La navegación principal tendrá 10 pantallas:

1. Servers: alta, edición, agrupación y selección de VPS.
2. Dashboard: salud general, CPU, RAM, disco, red, uptime, servicios y actividad reciente.
3. Containers: Docker/Compose, estados, consumo, logs y acciones seguras.
4. Deployments: perfiles de despliegue, preflight, ejecución, progreso e historial.
5. Logs: logs locales, Docker y journalctl con filtros y clasificación.
6. Storage: disco, imágenes, cache, volúmenes y directorios pesados.
7. Files: navegador SFTP y transferencias.
8. Terminal: consola SSH integrada orientada a administración puntual.
9. Security: checklist de SSH, firewall, actualizaciones, certificados y exposición básica.
10. Settings: apariencia, refresco, seguridad local, rutas y preferencias.

Alertas y actividad se mostrarán como panel lateral global y también se resumirán en Dashboard; no necesitan una pantalla separada inicialmente.

## Pantalla principal esperada

El shell tendrá:

- Sidebar de 224 px, colapsable a 72 px.
- Header de 64 px con servidor activo, entorno, latencia, estado SSH y acceso a alertas.
- Área de contenido con padding de 24 px.
- Dark theme como predeterminado y soporte Light.
- Inter como tipografía base.
- Tarjetas con radio de 12 px y contraste moderado, sin estética de terminal permanente.

Dashboard:

- Fila superior: CPU, RAM, Disco y Red.
- CPU con indicador radial compacto.
- RAM con barra + valor usado/total.
- Disco con donut + libre/usado.
- Red con RX/TX y mini tendencia.
- Gráfico temporal principal para CPU/RAM/Red.
- Panel de servicios/contenedores activos.
- Historial de deploys/operaciones.
- Estado de compatibilidad del VPS.

## Compatibilidad

No se implementarán clases de negocio acopladas a Hostinger salvo que una función use una API exclusiva del proveedor.

Cada servidor pasa por un Compatibility Check que detecta, entre otros:

- conectividad SSH;
- Linux y distribución;
- shell disponible;
- systemd;
- Docker;
- Docker Compose v2;
- journalctl;
- Git;
- Nginx;
- utilidades necesarias para métricas.

Los módulos se habilitan según capacidades. Un VPS sin Docker debe seguir pudiendo usar Dashboard, Files, Terminal, Logs del host y otras funciones compatibles.

## Telemetría

Las métricas deben ser numéricas desde Core/Application; nunca strings preformateados.

Frecuencias iniciales:

- CPU/RAM/Red: 2 s.
- Estado Docker: 5 s.
- Disco: 30 s.
- Compatibilidad: bajo demanda y al conectar.

Se conservará un ring buffer en memoria para datos recientes. El histórico persistente se agregará después con almacenamiento local.

Importante: el cálculo actual del Migrator basado en load average / cores no representa porcentaje real de CPU. v2 debe calcular CPU desde /proc/stat con dos muestras, dejando load average como métrica independiente.

## Seguridad

- Nunca guardar secretos en texto plano.
- Nunca escribir contraseñas, tokens o connection strings completas en logs.
- SecretVault abstracto; implementación inicial Windows mediante DPAPI.
- En otros sistemas, si no existe un vault seguro implementado, no persistir secretos.
- Acciones destructivas requieren confirmación explícita.
- Production debe tener guardrails más estrictos.
- Vista previa de comando para acciones delicadas.

## Fases

### Fase A - Base técnica

- Crear solución Avalonia.
- Crear Domain/Application/Infrastructure/Desktop.
- Implementar modelos de ServerProfile y capacidades.
- Implementar SSH genérico.
- Portar sanitización y clasificación de logs.
- Crear shell y Dashboard estático.

### Fase B - Conexión real y Dashboard

- Alta de servidor.
- Compatibility Check.
- Métricas reales numéricas.
- Historial corto de métricas.
- Gráficas LiveCharts2.
- Estado Docker genérico, sin nombres holos-*.

### Fase C - Operación diaria

- Containers.
- Logs.
- Storage.
- Files/SFTP.
- Terminal.

### Fase D - Deployments

- Definir DeploymentProfile separado de ServerProfile.
- Mantener soporte para scripts existentes como adaptador Legacy Script Runner.
- Crear pipeline de deploy genérico por pasos.
- Historial y guardrails.

### Fase E - Seguridad y productización

- Security checklist.
- Persistencia local segura.
- Instalador/portable.
- Telemetría histórica opcional.
- Pruebas con proveedores adicionales.

## Definition of Done del primer MVP

- Inicia como aplicación Avalonia nativa.
- Permite registrar un VPS Hostinger y conectarse por SSH.
- Muestra CPU, RAM, disco, red y uptime con gráficas.
- Detecta Docker y lista contenedores reales.
- Consulta logs sin exponer secretos.
- Permite seleccionar servidor sin editar .env.
- El código no contiene nombres holos-* en lógica genérica.
- Hostinger aparece como probado, no como dependencia técnica.
- El servidor no requiere instalar un agente VPS Desk.
