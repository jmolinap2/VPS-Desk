# Roadmap De Producto De VPS Desk

VPS Desk busca ser un panel de control de escritorio limpio para operar servidores VPS Linux mediante SSH. El producto no intenta reemplazar el panel del proveedor de hosting. Su objetivo es hacer visibles, repetibles y más seguras las operaciones diarias que normalmente se hacen con comandos: Docker, logs, deploys, backups, checks de salud y mantenimiento.

## Posicionamiento Del Producto

Proveedores como Hostinger ya ofrecen herramientas a nivel proveedor: backups semanales, snapshots, firewall, API pública, apps de un clic y terminal web con IA. VPS Desk debe enfocarse en la capa que viene después de crear el VPS: lo que ocurre dentro del servidor cuando ya estás operando tu aplicación.

Promesa central:

> Una interfaz de escritorio limpia para administrar la vida operativa real de un VPS Linux.

El valor no está en crear otro panel de hosting genérico. El valor está en convertir flujos comunes de línea de comandos en acciones visibles, seguras y repetibles.

## Principios Del Producto

- Empezar con un solo VPS y hacerlo excelente antes de agregar soporte multi-servidor.
- Priorizar visibilidad de solo lectura antes de acciones que modifican el servidor.
- Toda acción riesgosa debe tener vista previa del comando, confirmación y salida clara.
- SSH debe ser el transporte principal para mantener la app independiente del proveedor.
- Las APIs de proveedores deben ser integraciones opcionales, no una dependencia obligatoria.
- Los secretos deben permanecer locales y nunca imprimirse en logs.
- El diseño debe servir a operadores: compacto, directo, escaneable y con poca fricción.
- La UI debe simplificar sin ocultar detalles técnicos importantes.

## Fase 0 - Base Actual

Objetivo: definir lo que ya existe y estabilizarlo como punto de partida.

Capacidades actuales o parcialmente implementadas:

- Aplicación de escritorio para Windows construida con PowerShell 7 y WPF.
- Carga de configuración local desde `.env`.
- Perfiles de entorno para Development, Staging y Production.
- Verificación de disponibilidad SSH.
- Espacios de dashboard para CPU, memoria, disco y uptime.
- Detección de estado para contenedores Docker comunes: `sql`, `api` y `front`.
- Vista de almacenamiento con uso de disco, imágenes Docker y directorios Docker pesados.
- Visor de logs remotos para contenedores Docker y journals del sistema.
- Lanzador de deploys y migraciones sobre scripts PowerShell existentes.
- Guardas de producción para opciones inseguras.
- Enmascaramiento de secretos comunes en logs.
- Licencia MIT, README, política de seguridad y `.env.example` público.

Criterios de salida:

- La app inicia de forma confiable desde `Run.bat`.
- El repositorio público no contiene credenciales reales, logs ni configuración local.
- Las vistas usan el nombre VPS Desk de forma consistente.
- El README explica instalación, uso y expectativas de seguridad.

## Fase 1 - MVP Para Un Solo VPS

Objetivo: hacer que VPS Desk sea útil de verdad para un servidor sin inflar el alcance. Esta debe ser la primera meta de producto.

Prioridad recomendada: alta.

### 1. Shell De UI Limpio

- Mantener la navegación principal simple: Overview, Deploy, Docker, Logs, Storage, Security, Settings.
- Hacer que Overview sea la primera pantalla.
- Mostrar solo la información más importante en la primera vista.
- Usar tarjetas compactas de estado, no layout decorativo.
- Agregar colores consistentes: sano, advertencia, peligro, desconocido.
- Agregar estados de carga y offline en cada check remoto.

Criterios de aceptación:

- El usuario puede abrir la app y entender la salud del servidor en menos de 10 segundos.
- Los estados vacíos explican qué falta sin sonar como documentación larga.
- Ninguna vista requiere leer el código fuente para entender su propósito.

### 2. Onboarding Y Configuración De Conexión

- Agregar un flujo inicial para host, usuario, puerto y modo de autenticación.
- Validar host y puerto SSH antes de guardar.
- Detectar si `pwsh`, `ssh` y la llave privada existen.
- Guardar configuración no sensible en AppData.
- Mantener secretos fuera de AppData salvo que el usuario lo active explícitamente.

Criterios de aceptación:

- Un usuario nuevo puede configurar un VPS desde la UI.
- Los errores de conexión muestran mensajes accionables.
- La app puede funcionar con autenticación por llave sin contraseña en `.env`.

### 3. Overview Dashboard V1

- Estado online/offline del servidor.
- CPU, memoria, disco y uptime.
- Disponibilidad de Docker.
- Estado de servicios clave: API, frontend y base de datos.
- Resultado del último deploy.
- Hora del último check.
- Acciones rápidas: refrescar, abrir logs, abrir deploy.

Criterios de aceptación:

- El dashboard funciona aunque Docker no esté instalado.
- Los fallos SSH no congelan la UI.
- Las métricas muestran su fuente o la razón por la que no están disponibles.

### 4. Deploy Center V1

Deploy Center debe estar en Fase 1 porque el proyecto ya tiene conceptos de deploy y migración. También es uno de los diferenciales más fuertes: los proveedores suelen ofrecer herramientas del servidor, pero no tu flujo específico de despliegue de aplicación.

Alcance:

- Seleccionar target de deploy: API, frontend o ambos.
- Seleccionar branch.
- Ejecutar preflight checks.
- Backup opcional antes del deploy.
- Ejecutar el script de deploy existente.
- Transmitir salida en vivo.
- Mostrar pasos de progreso.
- Guardar historial local de deploys.
- Mostrar estado final y duración.

Requisitos de seguridad:

- Los deploys a Production requieren confirmación explícita.
- Las opciones destructivas o riesgosas muestran advertencias visibles.
- Cada deploy muestra el script exacto y los argumentos principales antes de ejecutar.
- Tokens y contraseñas deben enmascararse en la salida.

Criterios de aceptación:

- El usuario puede ejecutar un deploy normal a Staging desde la UI.
- Un deploy fallido deja suficiente contexto de logs para depurar.
- Production no permite ejecutar con skips inseguros activados.

### 5. Logs V1

- Cargar logs recientes desde contenedores Docker.
- Cargar logs del sistema mediante `journalctl`.
- Filtrar por texto.
- Resaltar líneas de error, warning y éxito.
- Copiar líneas seleccionadas.
- Limpiar la salida local de la UI sin borrar logs remotos.

Criterios de aceptación:

- El usuario puede responder: qué falló, dónde y cuándo.
- Los logs largos siguen siendo legibles.
- Los valores sensibles se enmascaran antes de mostrarse y antes de guardarse localmente.

### 6. Docker Status V1

- Listar contenedores con nombre, imagen, estado, puertos y política de reinicio.
- Mostrar salud rápida para roles comunes de la app.
- Permitir reiniciar de forma segura un contenedor seleccionado.
- Dejar stop/remove fuera de Fase 1 salvo que existan confirmaciones fuertes.

Criterios de aceptación:

- El usuario puede ver si el stack está corriendo.
- La acción de reinicio muestra nombre del contenedor y vista previa del comando.
- Los errores aparecen en la UI.

## Fase 2 - Operaciones Diarias

Objetivo: reemplazar el ciclo común de mantenimiento por comandos con herramientas visuales seguras. Esta fase hace que la app se sienta completa para un desarrollador solo o un equipo pequeño.

Prioridad recomendada: alta después de Fase 1.

### 1. Docker Manager V2

- Iniciar, detener, reiniciar y recrear contenedores.
- Descargar imágenes nuevas.
- Ejecutar `docker compose ps`, `pull`, `up -d`, `down` con vista previa del comando.
- Mostrar consumo de recursos por contenedor cuando sea posible.
- Mostrar edad y tamaño de imágenes.
- Detectar contenedores huérfanos.

Requisitos de seguridad:

- `down`, eliminación de volúmenes y prune de imágenes requieren confirmación fuerte.
- Preferir dry-run o preview cuando sea posible.
- Mostrar contenedores afectados antes de ejecutar acciones destructivas.

### 2. Backups A Nivel Aplicación

Este es un diferencial grande. Los snapshots del proveedor son útiles, pero los backups de aplicación son más precisos y portables.

Alcance:

- Presets de dump para PostgreSQL, MySQL/MariaDB y SQL Server cuando aplique.
- Backup de carpetas persistentes o uploads.
- Backup de Docker Compose y configuración de Nginx.
- Descargar el archivo de backup a la máquina local.
- Mostrar tamaño, fecha, hora y origen.
- Limpieza opcional por retención.

Criterios de aceptación:

- El usuario puede crear un backup antes de deploy.
- El usuario puede verificar dónde quedó guardado el backup.
- Los comandos de backup son visibles antes de ejecutarse.

### 3. Security Checklist V1

- Estado de autenticación SSH por password.
- Estado de login root.
- Puertos abiertos.
- Estado de UFW/firewall.
- Estado de Fail2ban si está instalado.
- Actualizaciones pendientes.
- Advertencia por presión de disco.
- Expiración SSL para dominios configurados.

Criterios de aceptación:

- La página Security genera una lista clara de ok, warning y fail.
- Cada advertencia incluye una acción recomendada.
- Los botones de corregir están separados de los botones de verificar.

### 4. Herramientas Web Y SSL

- Listar sitios Nginx o fragmentos de reverse proxy.
- Verificar resolución DNS.
- Verificar códigos HTTP y HTTPS.
- Mostrar emisor y fecha de expiración del certificado.
- Ejecutar renovación segura cuando Certbot esté detectado.

Criterios de aceptación:

- El usuario puede diagnosticar problemas comunes de dominio/SSL sin terminal.
- La app no sobreescribe configuraciones del servidor automáticamente en esta fase.

### 5. Limpieza De Storage V2

- Explicar uso de disco por directorio.
- Limpieza de imágenes y cache de build Docker con preview.
- Detección de tamaño de logs.
- Limpieza de backups antiguos por reglas de retención.
- Mostrar espacio recuperado después de limpiar.

Criterios de aceptación:

- Las acciones de limpieza muestran qué se va a afectar.
- El usuario puede recuperar espacio sin adivinar comandos.

## Fase 3 - Multi-Servidor Y Runbooks

Objetivo: pasar de un VPS a una pequeña flota manteniendo una UI tranquila. Esto no debe ser Fase 1 ni Fase 2 porque agrega complejidad de producto, manejo de estado y más superficie de seguridad.

Prioridad recomendada: media.

### 1. Gestor De Servidores

- Agregar, editar y eliminar perfiles de servidores.
- Guardar host, puerto, usuario, modo de autenticación y etiquetas.
- Soportar etiquetas como `prod`, `staging`, `cliente`, `personal`.
- Agrupar servidores por proyecto o cliente.
- Mostrar resumen de salud entre servidores.
- Prevenir acciones accidentales de producción sobre múltiples servidores.

Criterios de aceptación:

- El usuario puede cambiar de servidor sin editar `.env`.
- Los secretos permanecen locales y no se guardan por defecto.
- Cada acción muestra claramente el servidor seleccionado.

### 2. Runbooks

Los runbooks son flujos reutilizables expuestos como botones.

Ejemplos:

- Reiniciar API.
- Descargar y reiniciar frontend.
- Limpiar cache Docker segura.
- Respaldar base de datos.
- Revisar errores recientes.
- Renovar SSL.
- Actualizar paquetes.
- Reiniciar servidor con confirmación.

Criterios de aceptación:

- Los pasos del runbook se muestran antes de ejecutar.
- Cada paso tiene estado, salida y duración.
- Los pasos fallidos detienen el flujo salvo configuración explícita.

### 3. Programación Y Notificaciones

- Checks de salud programados.
- Notificaciones locales de escritorio.
- Notificaciones opcionales por email, webhook, Discord o Slack.
- Reglas de alerta para disco, offline, deploy fallido y expiración SSL.

Criterios de aceptación:

- Las alertas se configuran sin editar código fuente.
- Las notificaciones no incluyen secretos.
- El usuario puede desactivar checks en segundo plano.

## Fase 4 - Integraciones Y Productización

Objetivo: hacer que VPS Desk se sienta instalable, confiable y extensible.

Prioridad recomendada: media a baja hasta que el flujo principal sea fuerte.

### 1. Empaquetado

- Instalador o zip portable.
- Número de versión visible en la UI.
- Changelog.
- Auto-update o aviso de actualización.
- Firmado de releases si la distribución crece.

### 2. Integraciones Con Proveedores

Las APIs de proveedores deben mejorar la app, no reemplazar las operaciones por SSH.

Integraciones posibles:

- API pública de Hostinger para metadata del VPS cuando esté disponible.
- Visibilidad de snapshots y backups si el proveedor lo expone por API.
- Sincronización de reglas de firewall si el proveedor lo expone por API.
- Plan, región e información de ancho de banda del servidor.

Criterios de aceptación:

- La app sigue funcionando sin credenciales del proveedor.
- Las funciones de proveedor son opcionales por perfil de servidor.
- SSH sigue siendo la capa operativa principal.

### 3. Plugins O Packs De Scripts

- Comandos definidos por el usuario con metadata.
- Definición de runbooks en YAML o JSON.
- Importar/exportar packs de comandos seguros.
- Plantillas comunitarias para stacks comunes: Docker Compose, Laravel, Node, .NET, WordPress, n8n y flujos cercanos a Coolify.

Criterios de aceptación:

- Los comandos personalizados requieren preview y confirmación.
- Los packs importados no pueden ejecutar comandos silenciosamente.

### 4. Capa De Asistente IA

La IA debe agregarse solo después de tener comandos, logs y límites de seguridad sólidos.

Alcance posible:

- Explicar errores desde logs seleccionados.
- Sugerir comandos, pero exigir aprobación manual.
- Generar borradores de runbooks.
- Resumir la salud del servidor.

No objetivos:

- No acciones destructivas autónomas.
- No enviar secretos a servicios externos.
- No ejecución oculta de comandos.

## Fase 5 - Equipos Y Operación Avanzada

Objetivo: soportar equipos pequeños o agencias que administran VPS de clientes.

Prioridad recomendada: posterior.

Alcance:

- Perfiles locales por rol.
- Exportación de auditoría.
- Notas por servidor y documentos de handoff.
- Agrupación por cliente/proyecto.
- Librería compartida de runbooks.
- Exportación/importación cifrada de settings.
- Timeline de incidentes, outages y deploys.

Criterios de aceptación:

- Un segundo operador puede entender qué pasó en un servidor.
- El historial de auditoría incluye acción, servidor, timestamp y resultado.
- Los valores sensibles nunca se incluyen en exportaciones por defecto.

## Orden Sugerido De Construcción

1. Pulir la UI actual de un solo servidor y renombrar etiquetas internas restantes.
2. Construir el setup inicial de conexión.
3. Hacer Overview útil y confiable.
4. Convertir el launcher actual en Deploy Center V1.
5. Mejorar Logs V1 y Docker Status V1.
6. Agregar backups a nivel aplicación.
7. Agregar Security Checklist V1.
8. Agregar Docker Manager V2 y limpieza de Storage V2.
9. Agregar Gestor de Servidores solo cuando el flujo de un servidor sea excelente.
10. Agregar APIs de proveedor, empaquetado y plugins después de estabilizar el flujo central.

## No Objetivos Para Las Primeras Fases

- Reemplazo completo de cPanel/Plesk.
- Administración de cuentas de hosting web.
- Administración de DNS de registrador.
- Hosting de email.
- File manager completo.
- Administración autónoma por IA.
- Permisos multiusuario de equipo.
- Administración de Kubernetes.

Estos puntos pueden revisarse después, pero agregarlos temprano haría la app más difícil de terminar y menos enfocada.

## Diferenciadores

- Operaciones VPS desktop-first para usuarios de Windows.
- Flujo por SSH independiente del proveedor.
- Deploy Center específico de aplicación, no solo controles genéricos del servidor.
- Backups a nivel aplicación, no solo snapshots del proveedor.
- Vista previa de comandos y confirmaciones de seguridad.
- Logs limpios y troubleshooting práctico.
- Funciona con VPS comunes, no solo con un proveedor.

## Definición Del MVP

El MVP está listo cuando un usuario puede conectar un VPS y completar con confianza este ciclo desde la UI:

1. Revisar salud del servidor.
2. Inspeccionar servicios Docker.
3. Leer logs recientes.
4. Ejecutar un deploy a Staging.
5. Confirmar que la app quedó sana.
6. Crear o verificar un backup.
7. Ver advertencias obvias de seguridad.

Si ese ciclo funciona de forma fluida, VPS Desk ya es útil.
