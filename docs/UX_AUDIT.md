# Auditoría de UI/UX — noviembre 2026

Revisión del estado de la interfaz frente a las normas de [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) y
frente a lo que se espera de una app móvil de calidad (Facebook, Revolut, la app de GitHub,
Termius). Incluye el plan por fases para cerrar la brecha.

**Método:** medición sobre el código de `src/VpsDesk.UI` y `src/VpsDesk.Android` (búsquedas y
conteos reproducibles, ver §6), cálculo de contraste WCAG de la paleta usada, y uso de la app
en un POCO X6 Pro durante el rediseño de Respaldos. Las capturas nuevas quedan pendientes de
la fase 0.

---

## 1. Resumen

La base es buena: tema oscuro coherente, Inter, variantes móviles para 12 de 13 pantallas, hojas
inferiores y cajón animados, manejo del teclado y confirmaciones fuertes en lo peligroso. Lo que
separa a VPS Desk de una app "premium" no es falta de funciones, sino tres cosas:

1. **No hay sistema de diseño en el código.** 135 colores distintos, 28 tamaños de letra y 25
   radios escritos a mano. Cada pantalla es "casi igual" a la otra, y ese "casi" es lo que se
   percibe como falta de madurez.
2. **En Android no se comporta como una app de Android.** El botón atrás no se maneja, no hay
   gesto de refresco, ni vibración, ni iconos reales, y los objetivos táctiles son pequeños.
3. **Accesibilidad y localización a medias.** Dos etiquetas de accesibilidad en toda la app,
   textos de 9–10 px, grises con contraste insuficiente y ~450 textos sin traducir.

Las tres se resuelven sin rehacer pantallas: primero fundamentos (tokens), después
comportamiento nativo, y al final flujo y pulido.

## 2. Puntuación por área

Escala 1 (ausente) a 5 (al nivel de una app de referencia).

| Área | Nota | Comentario |
|---|---|---|
| Seguridad de acciones | **4** | Frase `RESTAURAR`, copia previa, nombre del servidor en confirmaciones. Punto fuerte. |
| Movimiento | 3 | Hay transiciones de página, hojas y cajón, pero con 10 duraciones distintas y sin física. |
| Color y contraste | 3 | Paleta oscura coherente; dos grises muy usados no llegan a AA. Dos acentos distintos. |
| Rendimiento percibido | 3 | SSH fuera del hilo de UI; listas dentro de `ScrollViewer` pierden virtualización. |
| Consistencia visual | 2 | Sin tokens; estilos de hoja duplicados en 3 vistas con valores distintos. |
| Tipografía | 2 | 28 tamaños; 52 usos de 9–10,5 px solo en vistas móviles. Mono inexistente en Android. |
| Navegación móvil | 2 | Cajón con 13 destinos: todo queda a dos toques y oculto. |
| Estados carga/vacío/error | 2 | Mayormente texto suelto arriba ("Consultando…"). Sin esqueletos ni estados vacíos ilustrados. |
| Localización | 2,5 | Infraestructura sólida, pero ~200 cadenas fijas en 25 ViewModels y ~246 literales en XAML. |
| Iconografía | 1,5 | Caracteres Unicode como iconos; cambian según el dispositivo. |
| Integración Android | 1,5 | Sin botón atrás, sin color de barras del sistema ni fondo de arranque, sin vibración. |
| Accesibilidad | 1 | 2 `AutomationProperties` en total; botones de icono mudos para TalkBack. |

## 3. Hallazgos

Prioridad: **P1** afecta el uso o la seguridad · **P2** se nota como falta de calidad ·
**P3** pulido.

### 3.1 Fundamentos visuales

| # | P | Hallazgo | Evidencia | Norma |
|---|---|---|---|---|
| F1 | P2 | 877 colores hexadecimales escritos a mano (135 distintos) en vistas y `App.axaml`; 46 más en C#. | `Views/*.axaml`, `App.axaml` | DS §3.1 |
| F2 | P2 | Dos acentos: azul cielo `#38BDF8` en el sidebar de escritorio y verde azulado `#0F766E`/`#5EEAD4` en el resto. | `App.axaml:25`, `MobileStyles.axaml` | DS §3.1 |
| F3 | P1 | Grises de etiqueta bajo AA: `#66758A` (119 usos, 3,78:1 sobre tarjeta) y `#56657A` en títulos de sección (2,99:1). | Contraste calculado | DS §3.1, §8 |
| F4 | P2 | Hojas inferiores más oscuras que las tarjetas (`#10141D` < `#121826`): la elevación se lee al revés. | `MobileStyles.axaml:142` | DS §3.1 |
| F5 | P1 | 28 tamaños de letra; 165 usos de 8,5–10,5 px, 52 de ellos en vistas móviles. | `FontSize=` en `Views/` | DS §3.2 |
| F6 | P2 | `Cascadia Mono,Consolas` no existe en Android: rutas, logs y terminal usan la mono genérica del sistema. | 51 usos de `FontFamily` | DS §3.2 |
| F7 | P2 | 25 radios distintos, de 1 a 24 px, incluidos valores como 1,2 / 11 / 13 / 17 / 21. | `CornerRadius=` | DS §3.4 |
| F8 | P2 | 10 duraciones de animación distintas (0,12–0,38 s). | `Duration=` | DS §3.5 |
| F9 | P2 | Iconos con caracteres Unicode (☰ ↻ › ✕ ✓ ▤ ⌄ ⌃ ← ↑). | `MobileShell.axaml:73,82` y otros | DS §3.6 |
| F10 | P2 | `Border.sheet`/`Border.scrim` redefinidos en 3 vistas con recorridos y tiempos distintos (650 px/0,28 s frente a 900 px/0,38 s). | `MobileContainersView`, `MobileDeploymentsView`, `MobileShell` | DS §4.3 |
| F11 | P3 | El tema claro figura como soportado en UI_SPEC_V2, pero `RequestedThemeVariant="Dark"` está fijo y los colores escritos a mano lo impiden. | `App.axaml:4` | DS §3.1 |

### 3.2 Comportamiento en Android

| # | P | Hallazgo | Evidencia | Norma |
|---|---|---|---|---|
| A1 | **P1** | El botón/gesto atrás no se maneja: con una hoja o el cajón abiertos, atrás no los cierra; Android cierra la actividad. | Sin `BackRequested` en todo `src/` | DS §5 |
| A2 | P1 | Objetivos táctiles por debajo de 48: refrescar 38–40 px, ítems del cajón ≈ 40 px. | `MobileShell.axaml:80`, `MobileContainersView.axaml:59`, `App.axaml:13` | DS §4.1 |
| A3 | P2 | Sin gesto de arrastrar para refrescar; el refresco es un ↻ distinto en cada pantalla. | Sin `RefreshContainer` | DS §5 |
| A4 | P2 | Navegación solo por cajón con 13 destinos; ningún destino frecuente a un toque. | `MobileShell.axaml:138-160` | DS §5 |
| A5 | P2 | Sin respuesta háptica. | Sin `Vibrat`/`Haptic` en `src/` | DS §5 |
| A6 | P2 | Tema Android sin color de barras del sistema ni `windowBackground`: posible destello y barra de estado que no combina. `targetSdkVersion` 36 obliga a revisar edge-to-edge en Android 15+. | `Resources/values/styles.xml`, `AndroidManifest.xml` | DS §5 |
| A7 | P2 | Respaldos es la única pantalla sin variante móvil: usa la vista de escritorio dentro del teléfono. | `MobilePageHost.axaml:52` | DS §5 |
| A8 | P3 | Las acciones de cada elemento son botones en fila; sin deslizar ni mantener pulsado. | Vistas de lista móviles | DS §5 |

### 3.3 Estados, contenido y accesibilidad

| # | P | Hallazgo | Evidencia | Norma |
|---|---|---|---|---|
| E1 | P2 | Carga y error como texto suelto en la cabecera (p. ej. `ContainerModule.StatusMessage`); sin esqueletos. | `MobileContainersView.axaml:56` | DS §4.7 |
| E2 | P2 | Estados vacíos sin icono ni acción en la mayoría de listas (3 vistas móviles tienen alguno). | `Views/Mobile*.axaml` | DS §4.7 |
| E3 | P1 | Solo 2 `AutomationProperties` y 12 `ToolTip.Tip` en toda la UI: los botones de icono no tienen nombre para TalkBack. | `Views/*.axaml` | DS §8 |
| E4 | P2 | ~200 cadenas en español fijas en 25 ViewModels (p. ej. `DeploymentsViewModel.Database.cs:77,98,151`) y ~246 literales en XAML (`"SERVIDOR"`, `"Sin proyecto activo"`…). En inglés, la app sale mezclada. | Conteo con `grep` | DS §7 |
| E5 | P3 | Los puntos de estado de contenedores comunican solo con color (verde/gris). | `MobileContainersView.axaml:107` | DS §3.1 |
| E6 | P3 | 12 vistas ponen listas dentro de `ScrollViewer`; en listas largas (archivos, contenedores) se pierde la virtualización. | `BackupsView`, `MobileFilesView`, `MobileContainersView`… | DS §4.2 |

### 3.4 Lo que ya está bien y hay que conservar

- Confirmación por frase escrita y copia previa en restauraciones (nivel 3 de DS §9).
- Teclado virtual: el contenido se encoge con transición (`MobileShell.ApplyKeyboardInset`).
- Cambio de página con fundido y desplazamiento corto (`MobilePageHost`).
- Hojas y cajón animados solo con `Opacity`/`RenderTransform`.
- `UserFacingErrors` como punto único para traducir excepciones a mensajes.
- Variantes móviles reales (una columna, tarjetas) en lugar de escalar el escritorio.

## 4. Frente a una app móvil de referencia

| Qué hace una app de referencia | VPS Desk hoy | Fase |
|---|---|---|
| Un sistema de diseño: cada valor sale de un token | Valores escritos a mano en cada vista | 0 |
| Iconos vectoriales propios, mismo grosor en todo | Caracteres Unicode | 0 |
| Atrás cierra lo último que se abrió | Atrás cierra la app | 1 |
| Arrastrar para refrescar, con vibración al soltar | Botón ↻ | 1 |
| Esqueletos mientras carga, nunca pantalla quieta | Texto "Consultando…" | 1 |
| Destinos frecuentes a un toque (barra inferior) | Cajón con 13 entradas | 2 |
| Deslizar un elemento para la acción rápida | Botones en fila | 2 |
| Animaciones coreografiadas (escalonado, cifras que cuentan) | Transiciones sueltas | 3 |
| Tema claro y oscuro, icono temático de Android 13 | Solo oscuro | 3 |

La distancia más grande no está en las animaciones, sino en **consistencia** e **integración con
el sistema**. Son también las mejoras más baratas.

## 5. Plan por fases

Cada fase se puede publicar por separado (APK + escritorio) y no rompe las anteriores.

### Fase 0 — Fundamentos (sin cambiar flujos)

1. Crear `Themes/Tokens.axaml` con los tokens de DS §3 (colores con `ThemeDictionaries` Dark/Light).
2. Crear `Themes/Icons.axaml` con el set vectorial y reemplazar los caracteres Unicode (F9).
3. Mover `App.axaml` y `MobileStyles.axaml` a `Themes/Controls|Mobile|Desktop.axaml` usando tokens;
   unificar hojas y velo (F10).
4. Migrar vistas a tokens, empezando por las móviles (F1, F3, F5, F7, F8). Corrige contraste y
   tamaños mínimos de paso.
5. Incrustar JetBrains Mono (F6).
6. `AutomationProperties.Name` + `ToolTip.Tip` en botones de icono (E3).
7. Añadir `Scripts/Validate-UiTokens.ps1` que falle si aparecen `#RRGGBB`, `FontSize`,
   `CornerRadius` o `Duration` literales en `Views/` (como `Validate-Localization.ps1`).

**Hecho cuando:** 0 colores literales en `Views/`; solo los 8 tamaños de la escala; el validador
pasa; capturas antes/después de cada pantalla móvil.

### Fase 1 — Sensación nativa en Android

1. Botón atrás con la pila de DS §5 (A1).
2. Barras del sistema, `windowBackground` y splash con `Color.Canvas`; revisar zonas seguras en
   Android 15+ (A6).
3. `RefreshContainer` en Panel, Contenedores, Respaldos, Archivos, Logs y Almacenamiento (A3).
4. `IHapticFeedback` en UI e implementación en `VpsDesk.Android` (mismo patrón que
   `AndroidAppUpdatePlatform`) (A5).
5. Objetivos táctiles de 48 (A2).
6. Componentes `Skeleton`, `EmptyState` y `ErrorState` y su uso en las vistas remotas (E1, E2).

**Hecho cuando:** atrás nunca cierra la app con algo abierto; toda lista remota se refresca
arrastrando; ninguna vista remota muestra solo texto mientras carga.

### Fase 2 — Navegación y flujo (validar en el teléfono antes de cerrar)

1. Barra inferior: **Panel · Contenedores · Despliegues · Terminal · Más** (propuesta; ajustar
   según los destinos que más uses). El cajón pasa a ser "Más" (A4).
2. Selector de servidor como hoja al tocar el nombre en la barra superior.
3. `MobileBackupsView`: arriba "Crear copia" y "Última copia"; lo avanzado (contenedor, base,
   rotación, instantáneas, zona de peligro) detrás de un toque (A7).
4. Hoja de acciones por elemento y deslizar para la acción rápida en contenedores y archivos (A8).

### Fase 3 — Pulido premium

1. Coreografía: escalonado de listas, cifras de métricas que cuentan hasta su valor, transición
   compartida lista → detalle donde aporte.
2. Números tabulares en métricas y tamaños.
3. Tema claro completo y selector en Ajustes; preferencia de reducir animaciones.
4. Icono monocromo/temático de Android 13.
5. Escritorio: paleta de comandos `Ctrl+K`, atajos `Ctrl+1…9`, foco visible.
6. Localización completa de ViewModels (E4).
7. Página "Galería de componentes" solo en Debug, con todos los componentes y estados, para
   revisar cambios visuales de un vistazo.

### Métricas de seguimiento

| Métrica | Hoy | Objetivo |
|---|---|---|
| Colores literales en `Views/` | 877 | 0 |
| Tamaños de letra distintos | 28 | 8 |
| Radios distintos | 25 | 5 |
| Duraciones distintas | 10 | 4 |
| `AutomationProperties` | 2 | todos los botones de icono |
| Cadenas fijas en ViewModels | ~200 | 0 |
| Pantallas móviles sin variante propia | 1 | 0 |

## 6. Cómo repetir las mediciones

Desde `src/VpsDesk.UI` en Git Bash:

```bash
grep -ohE '#[0-9A-Fa-f]{6,8}\b' Views/*.axaml App.axaml | wc -l                     # colores literales
grep -ohE '#[0-9A-Fa-f]{6,8}\b' Views/*.axaml App.axaml | tr a-f A-F | sort -u | wc -l  # colores distintos
grep -ohE 'FontSize="[0-9.]+"' Views/*.axaml | sort -u | wc -l                      # tamaños de letra
grep -ohE 'CornerRadius="[^"]+"' Views/*.axaml | sort -u | wc -l                    # radios
grep -ohE 'Duration="[^"]+"' Views/*.axaml | sort -u | wc -l                        # duraciones
grep -o 'AutomationProperties' Views/*.axaml | wc -l                                # accesibilidad
LC_ALL=C.UTF-8 grep -ohP '"[^"\n]*[áéíóúñ¿¡…][^"\n]*"' ViewModels/*.cs | wc -l       # cadenas fijas (aprox.)
```

## 7. Avance (12/11/2026)

Primera pasada de la **fase 0 y 1** sobre Despliegues y Respaldos, verificada con un banco visual
(ventana oculta, ViewModel real y SSH simulado) en móvil (412 px) y escritorio (1180 px).

| Hallazgo | Estado |
|---|---|
| Modal de prevuelo sin salida (obligaba a cerrar la app) | **Resuelto.** Causa: la conexión SSH síncrona ignoraba cancelación y plazo, y el botón de cierre dependía de `IsBusy`. Ahora el ✕ cancela, hay plazo de 45 s, el ejecutor abandona la espera y todo modal se cierra con ✕, tocando el fondo o con «atrás». |
| A1 · botón atrás | **Implementado** con `IBackDismissible` (cada módulo cierra su capa más alta) en Despliegues, Respaldos, Archivos, Almacenamiento, Contenedores y hojas de Servidores. **Verificado** solo en Despliegues y Respaldos; el resto compila pero falta probarlo en el teléfono. Pendiente: Proyectos, Registros, Terminal, Seguridad y Ajustes si añaden capas. |
| F1/F7/F8 · tokens | **Parcial.** `Themes/Tokens.axaml`, `Icons.axaml` y `Controls.axaml` existen y los usan Despliegues (móvil y escritorio) y los modales de Respaldos. El resto de vistas sigue con valores literales. |
| F2 · dos acentos | **Resuelto en el tema**: el acento de Fluent (pestañas, casillas, interruptores) ya no toma el color del sistema (era morado en Windows), es el verde azulado del producto. |
| F4 · hoja más oscura que la tarjeta | **Resuelto** (`m-sheet` usa `Surface.Raised`). |
| F9 · iconos Unicode | **Parcial**: sustituidos en Despliegues, modales y Proyectos; quedan en el resto. |
| E1/E2 · estados de carga/vacío | **Parcial**: estado vacío del historial y progreso con barra en modales. Faltan esqueletos. |
| Estados de despliegue en inglés | **Resuelto**: 5 mensajes localizados (es/en) y clasificación de tono ampliada. |
| Resultado del historial en inglés (`Success/Failed`) | **Resuelto** con `OutcomeLabelConverter`. |
| Prevuelo/Desplegar ocultos en escritorio con la tarjeta contraída | **Resuelto**: barra de acciones fija. |

Pendiente de esta pasada: arrastrar para refrescar, vibración, esqueletos, barra inferior de
navegación, variante móvil de Respaldos y migrar el resto de vistas a tokens. Ver §5.
