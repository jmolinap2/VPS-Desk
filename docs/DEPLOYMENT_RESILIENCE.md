# Despliegues resistentes a cortes de red

**Estado:** implementado el 26 de noviembre de 2026 y probado contra un servidor SSH real en Docker, con cortes de red
reales (ver «Cómo se probó»). **No** se probó contra un VPS real, ni con la aplicación Android, ni con tiempos de
internet reales de horas.

## Problema

Un despliegue corre pasos largos en el VPS (`docker compose pull`, `up --build`, migraciones). Antes, cada paso viajaba por **una
sola sesión SSH**: si el internet del equipo se caía a mitad de un paso, la sesión se cerraba, el comando remoto moría con ella
(medido: `up` no sigue tras el corte) y el pase quedaba **a medias**. Los pasos no se podían repetir solos porque no se sabía si
el comando seguía corriendo.

## Cómo funciona ahora

Con `DetachLongSteps = true` (la aplicación de escritorio lo activa), los pasos que **no** se pueden repetir sin efectos corren
**desacoplados** de la sesión (`DetachedRemoteJob`):

1. **Inicio corto.** Una conexión SSH crea `~/.vpsdesk/jobs/<id>/`, escribe el comando y lo lanza con `setsid nohup`. El inicio es
   idempotente por id: repetirlo tras perder la respuesta no lanza el comando dos veces.
2. **El comando sigue en el servidor** aunque se cierre la conexión. Su salida va a `out` y su código de salida a `exit`.
3. **Consultas cortas.** El cliente consulta cada 1,5 s con conexiones nuevas e independientes: lee solo la salida nueva (por
   desplazamiento en bytes) y el código de salida. Si una consulta falla, el comando **no se repite**: se espera con retroceso
   (2, 4, 8, 15 s) y se vuelve a consultar. La salida llega completa, en orden y sin líneas repetidas.
4. **Plazo sin conexión:** hasta 10 minutos. Pasado el plazo, el paso queda **«sin confirmar»** (no «fallido»): el comando sigue
   o terminó en el servidor. Se informa el id del trabajo y la verificación posterior de recuperación revisa el estado real.
   Solo un rechazo que esperar no arregla (autenticación, huella del servidor) se informa de inmediato.
5. **Cancelar o superar el plazo del paso** detiene el grupo de procesos en el servidor (no quedan procesos huérfanos).
6. Al terminar se borran los archivos del trabajo.

`DetachedRemoteJob.AttachAsync(jobId)` vuelve a consultar un trabajo ya iniciado, por ejemplo tras reabrir la aplicación.

Los pasos de solo lectura (`verify_revision`, `compose_ps`) siguen usando sus reintentos de siempre; no se desacoplan.

## Lo que todavía no hace

- **La interfaz no ofrece «Reanudar».** El id del trabajo se informa en la salida del paso, pero la aplicación no lo guarda en el
  registro de recuperación ni tiene un botón para reanudar tras cerrar la aplicación. Tras un corte largo, la ruta es la
  verificación de recuperación existente y, si hace falta, repetir el pase (con Compose, `up -d` converge; las migraciones piden
  revisión manual).
- **Dependencias del servidor:** `setsid` (o `nohup`), `base64`, `wc`, `tail`, `head` y un `sh` POSIX. Probado en Debian 12 (`dash`).
  Sin `setsid` se usa `nohup` solo (sin grupo propio: cancelar mata únicamente al lanzador).
- Una consulta que parta un carácter UTF-8 multibyte entre dos lecturas puede mostrar un símbolo roto en pantalla; no afecta al
  resultado ni al código de salida.
- **Android** conserva su servicio en primer plano; el desacoplamiento aplica igual porque está en la capa de aplicación, pero no
  se probó en un teléfono.

## Cómo se probó

`Scripts/SshResilienceE2E` levanta un sshd real en Docker (`vpsdesk-e2e-sshd`, solo local, contraseña desechable), con un `docker`
simulado que tarda lo necesario, y un **proxy TCP que corta la red de dos formas**: cerrando las conexiones y, la más realista,
**«agujero negro»** (los paquetes se pierden y nada responde). Se ejecuta con `dotnet run --project Scripts/SshResilienceE2E`
(requiere Docker; `E2E_ONLY=3,4` filtra escenarios y `E2E_TRACE=1` traza cada llamada SSH).

Escenarios: paso corto; paso que falla (código de salida propio); corte de 8 s y de 25 s a mitad del paso (salida completa, una sola
ejecución); corte más largo que la espera («sin confirmar» y reanudar por id); inicio idempotente; cancelar y plazo detienen el
proceso remoto; despliegue completo (`ComposeDeploymentService`, con el prevuelo real) con la red caída durante `up`; y, de
contraste, el mismo corte **sin** desacoplar, que hace fallar el despliegue y mata `up`.

Las pruebas de regresión con simulacros (`Scripts/SshRegression`) pasan todas. Estaban rotas **antes** de este cambio (sus peticiones no
traían el commit y la huella verificados que el servicio exige); se corrigieron. El modo desacoplado es opcional en la petición, así que
sus escenarios no cambian.

## Conexiones que no contestan

Un servidor que acepta el TCP y no contesta (red móvil inestable, sshd saturado, límite de conexiones nuevas) dejaba la conexión
esperando todo el plazo del paso y el paso fallaba con «Connection failed to establish within N milliseconds». Ahora cada intento de
conexión espera **una parte del plazo** (un tercio, mínimo 8 s) y se reintenta hasta 3 veces, así el total no se alarga. Es seguro
repetir porque la conexión vence antes de enviar el comando: nunca se repite un comando ya enviado. Las credenciales rechazadas y la
huella distinta siguen sin reintentarse. El escenario 8 del E2E lo comprueba con un proxy que deja sin respuesta las primeras
conexiones (`ToxicProxy.HangNext`); con el código anterior fallaba tras el plazo completo.

## Registro de actividad

`ActivityLog` (capa de aplicación) guarda qué pasó en la app: reintentos y fallos de conexión SSH, conexiones lentas, cada paso de un pase,
inicio, aborto, reinicio y desenlace del pase, y errores no controlados. Es un archivo JSON por línea en la carpeta de datos
locales (`activity.jsonl`, rota a 1 MB, se conservan 7 días) más 2000 eventos en memoria. Los textos se sanean con `LogSanitizer`,
no se registra el texto de ningún comando ni la salida completa, y los comandos SSH correctos no se registran (solo los que fallan).
En **Ajustes → Registro de actividad** se puede ver, filtrar «solo errores y avisos», copiar o guardar el informe (`ActivityReport`) y
borrarlo. Si falla algo, ese informe se pega tal cual en un ticket.

## Burbuja flotante del pase (Android 11+)

Mientras corre un pase, el servicio en primer plano publica una notificación de conversación con metadatos de burbuja
(`DeploymentBubble`). Si el usuario permite las burbujas de VPS Desk (Ajustes de Android → Apps → VPS Desk → Notificaciones →
Burbujas), el pase flota sobre otras apps con su estado y los botones «Abortar pase» y «Abrir VPS Desk» (`BubbleActivity`, una pantalla
nativa mínima, no la interfaz de Avalonia). El aborto pasa por `DeploymentBackgroundExecution.RequestAbort`, que solo existe mientras
hay un pase. Sin permiso o en Android anterior no ocurre nada: queda la notificación normal. **No se probó en un teléfono.**

