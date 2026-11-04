# Deuda técnica

Registro de problemas conocidos de VPS Desk, con su estado. Cada entrada indica la evidencia,
el riesgo y la corrección. Estado: **Resuelta**, **Parcial** o **Abierta**.

Última revisión: 2026-11-03.

---

## Resueltas

### TD-01 · Resultados importantes solo como texto silencioso — Resuelta
- **Evidencia:** todas las pantallas comunicaban errores y éxitos cambiando un `StatusMessage` o
  `ServerEditorMessage`. En escritorio el resultado de "Probar conexión" era un texto gris bajo el
  formulario, fuera de la vista si la ventana era pequeña; en móvil, detrás de la hoja del formulario.
- **Riesgo:** un fallo (por ejemplo una copia previa al despliegue que no se hizo) podía pasar
  desapercibido.
- **Corrección:** `NotificationCenter` + `ToastHost` (avisos flotantes en escritorio y móvil) y la
  fachada `Toast.Success/Info/Warning/Error`. Los resultados de operaciones remotas, errores y
  validaciones publican un aviso; el texto de estado sigue como registro persistente.
  Los avisos de error duran 14 s, se pueden descartar con un toque o con ✕, y su texto se puede
  seleccionar y copiar. Un mismo aviso repetido se agrupa (×N) en vez de apilarse.

### TD-02 · Mensajes de excepción crudos — Resuelta
- **Evidencia:** "Connection test failed: Attempted to access a missing method." se mostraba tal
  cual al usuario.
- **Corrección:** `UserFacingErrors.Describe` traduce los casos conocidos (componentes de versiones
  distintas, clave privada no encontrada, autenticación rechazada, red inaccesible, tiempo agotado,
  cancelación) a un texto con una acción posible. Los tipos de SSH.NET se reconocen por nombre para
  no acoplar la UI a esa librería. El resto de excepciones conserva su mensaje.

### TD-03 · Indicador de conexión fijo en verde — Resuelta
- **Evidencia:** el punto junto al nombre del servidor era siempre verde (escritorio y cajón móvil),
  incluso con "SSH Offline".
- **Corrección:** usa `ConnectionToBrushConverter`: verde en línea, rojo sin conexión, gris sin
  comprobar.

### TD-04 · Resultado de "Probar conexión" sin estado visual en escritorio — Resuelta
- **Corrección:** la tarjeta cambia a verde o rojo, muestra una barra de progreso mientras prueba y
  el texto es seleccionable. Igual que en móvil.

### TD-05 · Publicación incremental con ensamblados mezclados — Resuelta
- **Evidencia:** el exe publicado el 10/10 fallaba con `MissingMethodException` mientras el mismo
  código recién compilado funcionaba. Los `.pdb` de `Infrastructure` (09/10) y `Application` (10/10)
  tenían fechas distintas: señal de una publicación que no recompiló todo.
- **Corrección:** `Scripts/Publish-Desktop.ps1` compila con `--no-incremental`, publica y con
  `-Install` copia el exe conservando `VpsDesk.Desktop.exe.previous`.

### TD-06 · `.env` instalado con huella SSH antigua — Resuelta (con aviso permanente)
- **Evidencia:** `C:\VPS-Desk-win-x64\.env` conservaba la huella `9At8…` anterior a la reinstalación
  del VPS (Ubuntu 26.04).
- **Corrección:** se actualizó la huella en ese `.env`; `Publish-Desktop.ps1` avisa si la huella
  instalada difiere de la del repositorio.

### TD-07 · Copias de la aplicación guardadas en carpeta privada en Android — Resuelta
- **Evidencia:** en Android la carpeta "Documentos" de .NET es privada de la app, invisible para el
  gestor de archivos y se borra al desinstalar.
- **Corrección:** `MainApplication` define `VPSDESK_BACKUP_DIR` en `GetExternalFilesDir(null)/Backups`,
  la misma ubicación del `.env`, accesible con `adb pull` o por USB.

### TD-08 · Etiquetas de 8 px en la navegación — Resuelta
- **Corrección:** subidas a 10 px (SERVIDOR, PROYECTO ACTIVO, modo del proyecto) en escritorio y móvil.

---

## Abiertas

### TD-10 · La máquina de estados del despliegue depende del texto de `StatusMessage` — Abierta · Alta
- **Evidencia:** `DeploymentsViewModel.History.cs`, `OnStatusMessageChanged`, abre modales y marca el
  inicio del despliegue buscando frases ("preflight", "Deployment is running", "Verificando…
  contenedor") dentro del texto.
- **Riesgo:** traducir o reescribir un mensaje rompe el flujo sin error de compilación.
- **Corrección propuesta:** un enum `DeploymentPhase` publicado por el servicio de despliegue; el
  texto pasa a ser solo presentación.

### TD-11 · Textos de interfaz mezclados (inglés/español) fuera de la localización — Abierta · Media
- **Evidencia:** muchos mensajes de `StatusMessage` están escritos a mano en inglés o español dentro
  de los ViewModels en lugar de pasar por `LocalizationService`. `ConnectionStatus` es una cadena
  ("SSH Online") que los conversores interpretan con `Contains`.
- **Corrección propuesta:** claves en `Localization/Languages`, validadas con
  `Scripts/Validate-Localization.ps1`; `ConnectionStatus` como enum.

### TD-12 · `NotificationCenter.Shared` y `Toast` son estáticos — Abierta · Baja
- **Riesgo:** dificulta las pruebas y acopla los ViewModels a un singleton.
- **Corrección propuesta:** inyectar `INotificationService`; mantener `Toast` como atajo de la
  implementación por defecto.

### TD-13 · Sin proyecto de pruebas — Abierta · Alta
- **Evidencia:** la solución no incluye ningún proyecto de pruebas. La verificación de las copias de
  la aplicación se hizo con un arnés manual contra el VPS real.
- **Corrección propuesta:** pruebas unitarias de `UserFacingErrors`, de `NotificationCenter`
  (duplicados, límite, caducidad), del script de copia con un servidor simulado y de la rotación.

### TD-14 · Copias de la aplicación: huecos conocidos — Abierta · Alta
- **No hay restauración:** solo se crean y se descargan; restaurar es manual.
- **Cobertura:** no incluye los volúmenes de Redis ni `traefik-letsencrypt`.
- **Sin cifrar:** el `.tar` contiene el `.env` del proyecto (claves y tokens) en claro.
- **Android:** sin servicio en primer plano ni pantalla encendida; si el sistema suspende la app
  durante los ~70 s, la copia falla (la rotación no borra nada, pero puede quedar un `.tar`
  temporal en `/tmp` del VPS). Con datos móviles son ~130 MB por copia.
- **Corrección propuesta:** cifrado AES-GCM con contraseña, botón "Restaurar", incluir volúmenes,
  `FLAG_KEEP_SCREEN_ON` o servicio en primer plano en Android y aviso de uso de datos.

### TD-15 · Dos carpetas de publicación — Abierta · Baja
- `VPS-Desk-win-x64` (repositorio, ignorada por git) y `C:\VPS-Desk-win-x64` (instalada, con su
  `.env`). Hasta ahora la copia era manual; `Publish-Desktop.ps1 -Install` la automatiza.

### TD-16 · Avisos solo dentro de la aplicación — Abierta · Media
- Si VPS Desk está en segundo plano (o en el móvil con la pantalla bloqueada) no hay aviso del
  sistema al terminar una copia o un despliegue largo. Tampoco hay historial de avisos.
- **Corrección propuesta:** notificación del sistema en Windows y Android al terminar operaciones
  largas con la app sin foco, y un panel de historial de avisos.

### TD-17 · Accesibilidad y adaptación — Parcial · Media
- Quedan fuentes pequeñas (10–11 px) en etiquetas secundarias, contraste bajo en grises
  `#66758A` sobre fondo oscuro y botones de iconos por debajo de 44 px en móvil.
- Los avisos nuevos usan región en vivo (`LiveSetting`) y objetivo táctil de 36 px; el resto de la
  app no se ha revisado con lector de pantalla.

### TD-18 · Preferencias con guardado silencioso — Abierta · Baja
- `SettingsViewModel.Persist` guarda en cada cambio sin avisar. No se añadió aviso para no generar
  ruido en cada interruptor; conviene un indicador discreto "Guardado".

---

## Criterios de UX aplicados a los avisos

| Aspecto | Escritorio | Móvil |
|---|---|---|
| Posición | Esquina superior derecha, 400 px, bajo la cabecera | Ancho completo bajo la barra superior, 12 px de margen |
| Duración | Éxito 4 s · Info 5 s · Aviso 9 s · Error 14 s | Igual |
| Descartar | Clic en el aviso o en ✕ | Toque en el aviso o en ✕ (36 px) |
| Copiar el error | Texto seleccionable | Pulsación larga para seleccionar |
| Saturación | Máximo 4 visibles; repetidos se agrupan (×N) | Igual |
| Color | Verde éxito · ámbar aviso · rojo error · azul info, con icono además del color | Igual |

Un resultado nunca depende solo del color: lleva icono (✓ ! ✕ i) y título.
