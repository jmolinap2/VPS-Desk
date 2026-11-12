# Sistema de diseño y normas de UX

Fuente de verdad para todo lo visual y de interacción de VPS Desk (escritorio y Android).
Si este documento contradice a [UI_SPEC_V2.md](UI_SPEC_V2.md), manda este. UI_SPEC_V2 sigue
describiendo **qué** muestra cada pantalla; este documento define **cómo** se ve y se comporta.

El diagnóstico del estado actual y el plan para llegar aquí están en [UX_AUDIT.md](UX_AUDIT.md).

---

## 1. Principios

Una app "premium" no se nota por los adornos, sino porque nada desentona. En orden de prioridad:

1. **Seguro antes que rápido.** Opera servidores en producción. Ninguna acción destructiva puede
   ejecutarse por un toque accidental (ver §9).
2. **Consistente.** Mismo color, tamaño, radio y tiempo de animación para lo mismo en todas las
   pantallas. Ningún valor visual se escribe a mano en una vista: se usa un token (§3).
3. **Responde al instante.** Todo toque tiene respuesta visible en menos de 100 ms (estado pulsado,
   vibración, indicador). Una operación remota nunca deja la pantalla quieta sin explicación.
4. **Calma.** Fondo oscuro neutro, un solo color de acento, colores semánticos solo para estados.
   Sin brillos, sin texto "hacker", sin animaciones que no comuniquen algo.
5. **Una cosa por vez.** Cada pantalla tiene una acción principal. Lo avanzado se revela bajo
   demanda (expansores, hojas, "Más opciones"), no se muestra todo a la vez.
6. **Nativo en cada plataforma.** En Android se comporta como una app de Android (botón atrás,
   gestos, vibración, barras del sistema). En escritorio, como una app de escritorio (teclado,
   hover, menús contextuales).

---

## 2. Estructura de archivos (objetivo)

```
src/VpsDesk.UI/Themes/
  Tokens.axaml        colores (Dark + Light), tipografía, espaciado, radios, movimiento
  Icons.axaml         geometrías vectoriales (StreamGeometry) de un único set de iconos
  Controls.axaml      estilos base compartidos: botones, tarjetas, chips, inputs, listas
  Mobile.axaml        solo lo exclusivo del teléfono (hojas, barra inferior, objetivos táctiles)
  Desktop.axaml       solo lo exclusivo del escritorio (sidebar, hover, densidad)
```

`App.axaml` incluye `Tokens`, `Icons` y `Controls`. `MobileShell` añade `Mobile`; `MainShell`
añade `Desktop`. Hoy existen `App.axaml` (estilos de escritorio) y `Views/MobileStyles.axaml`
(estilos móviles) con valores duplicados; se migran a esta estructura (UX_AUDIT, fase 0).

---

## 3. Tokens

### 3.1 Color

Los colores se nombran por **rol**, nunca por tono. Se definen en `Tokens.axaml` dentro de
`ResourceDictionary.ThemeDictionaries` (claves `Dark` y `Light`) y se consumen con
`{DynamicResource ...}` para que el cambio de tema funcione en caliente.

| Token | Uso | Dark | Light |
|---|---|---|---|
| `Color.Canvas` | Fondo de la ventana | `#0B0E13` | `#F6F7F9` |
| `Color.Surface.Bar` | Barra superior, barra inferior, sidebar | `#0E1219` | `#FFFFFF` |
| `Color.Surface.Card` | Tarjetas, filas de lista | `#121826` | `#FFFFFF` |
| `Color.Surface.Inset` | Campos, bloques hundidos, código | `#0E131B` | `#EEF1F5` |
| `Color.Surface.Raised` | Hojas inferiores, diálogos, menús, avisos | `#161E2C` | `#FFFFFF` |
| `Color.Border.Subtle` | Bordes de tarjeta y separadores | `#1E2735` | `#E2E6EC` |
| `Color.Border.Strong` | Bordes de campos y botones secundarios | `#2A3444` | `#CBD2DC` |
| `Color.Text.Primary` | Texto principal | `#E8EDF5` | `#0F172A` |
| `Color.Text.Secondary` | Texto de apoyo | `#9AA4B2` | `#475569` |
| `Color.Text.Tertiary` | Etiquetas, metadatos, títulos de sección | `#8290A3` | `#5A6577` |
| `Color.Text.Disabled` | Solo controles deshabilitados | `#56657A` | `#94A3B8` |
| `Color.Accent` | Relleno del botón principal, selección | `#0F766E` | `#0F766E` |
| `Color.Accent.OnAccent` | Texto sobre `Color.Accent` | `#FFFFFF` | `#FFFFFF` |
| `Color.Accent.Text` | Enlaces, valores destacados, ítem activo | `#5EEAD4` | `#0F766E` |
| `Color.Accent.Subtle` | Fondo de chips/badges de acento | `#102A24` | `#E6F4F1` |
| `Color.Success` / `.Subtle` | Correcto, en ejecución, sano | `#34D399` / `#102A24` | `#047857` / `#E7F6EF` |
| `Color.Warning` / `.Subtle` | Aviso, degradado | `#FBBF24` / `#2A2410` | `#B45309` / `#FDF3E2` |
| `Color.Danger` / `.Subtle` | Error, destructivo, caído | `#F87171` / `#2A1416` | `#B91C1C` / `#FCEBEB` |
| `Color.Info` / `.Subtle` | Informativo, neutro con énfasis | `#60A5FA` / `#10203A` | `#1D4ED8` / `#E8EEFC` |
| `Color.Scrim` | Velo detrás de hojas y cajón | `#000000` al 60 % | `#0F172A` al 40 % |

Reglas:

- **El acento es verde azulado (`#0F766E` / `#5EEAD4`).** El azul cielo `#38BDF8` del sidebar de
  escritorio se reemplaza por `Color.Accent.Text`. El azul queda solo para `Color.Info`.
- **Contraste mínimo WCAG AA:** 4,5:1 para texto normal, 3:1 para texto ≥ 18 px o ≥ 14 px en
  negrita y para iconos. Todos los pares de la tabla lo cumplen sobre `Surface.Card` y `Canvas`.
  `Color.Text.Disabled` (2,99:1) **no** se usa para texto que haya que leer.
- **Elevación en tema oscuro = superficie más clara**, no sombra. Lo que flota (hojas, diálogos)
  usa `Surface.Raised`, que es más clara que `Surface.Card`, más una sombra suave
  (`BoxShadow="0 8 24 0 #66000000"`).
- **Nunca solo color para comunicar un estado.** Un punto verde/rojo va acompañado de texto o
  de un icono distinto (accesibilidad y daltonismo).
- **Sin negro puro ni blanco puro** en superficies. Sí se permite blanco en `Accent.OnAccent`.

### 3.2 Tipografía

Fuente: **Inter** (ya registrada con `WithInterFont()`). Monoespaciada: **JetBrains Mono**
incrustada como recurso (`Cascadia Mono` y `Consolas` no existen en Android y hoy caen a la fuente
monoespaciada genérica del sistema).

| Token (clase) | Tamaño / interlineado | Peso | Uso |
|---|---|---|---|
| `t-display` | 28 / 34 | SemiBold | Cifra grande de una métrica |
| `t-title` | 20 / 26 | SemiBold | Título de pantalla |
| `t-headline` | 16 / 22 | SemiBold | Título de tarjeta u hoja |
| `t-body` | 14 / 20 | Regular | Texto principal (mínimo en móvil) |
| `t-body-s` | 13 / 18 | Regular | Texto secundario, descripciones |
| `t-label` | 12 / 16 | Medium | Metadatos, etiquetas de campo, chips |
| `t-overline` | 11 / 14 | SemiBold, +0,5 tracking | Títulos de sección ("Destino", "Protecciones") |
| `t-mono` | 12,5 / 18 | Regular | Rutas, comandos, hashes, logs |

Reglas:

- **Nada por debajo de 11 px.** Los tamaños 8,5 / 9 / 9,5 / 10 / 10,5 están prohibidos.
- Solo los 8 tamaños de la escala. Si un diseño "necesita" otro tamaño, el problema es la
  jerarquía, no la escala.
- Las cifras que cambian (CPU, RAM, tamaños, contadores) usan números tabulares
  (`FontFeatures="tnum"`) para que no "bailen" al actualizarse.
- Sin mayúsculas forzadas (Avalonia no tiene `text-transform`): botones, títulos y `t-overline` van en
  tipo oración ("Crear copia", no "CREAR COPIA" ni "Crear Copia"). Siglas como PROD sí en mayúsculas.

### 3.3 Espaciado

Retícula de 4 px. Valores permitidos: **4, 8, 12, 16, 20, 24, 32, 40**.

| Uso | Móvil | Escritorio |
|---|---|---|
| Margen lateral de pantalla | 16 | 24 |
| Relleno de tarjeta | 16 | 20 |
| Separación entre tarjetas | 12 | 16 |
| Separación dentro de un grupo (etiqueta–campo, ícono–texto) | 8 | 8 |
| Separación entre secciones | 24 | 32 |

### 3.4 Radios

| Token | Valor | Uso |
|---|---|---|
| `Radius.S` | 6 | Badges, chips pequeños, barras de progreso |
| `Radius.M` | 10 | Botones, campos, combos |
| `Radius.L` | 16 | Tarjetas, filas de lista |
| `Radius.XL` | 24 | Esquinas superiores de hojas inferiores, diálogos móviles |
| `Radius.Pill` | 999 | Chips de filtro, píldoras de estado |

Regla de anidación: un elemento dentro de otro redondeado usa un radio menor
(radio interior ≈ radio exterior − relleno).

### 3.5 Movimiento

Animar **solo `Opacity` y `RenderTransform`** (son baratas en Skia). Nunca `Width`, `Height`,
`Margin` ni `BoxShadow`.

| Token | Duración | Curva | Uso |
|---|---|---|---|
| `Motion.Instant` | 100 ms | `CubicEaseOut` | Estado pulsado (`scale(0.97)`), cambio de color |
| `Motion.Quick` | 160 ms | `CubicEaseOut` | Hover, interruptores, aparición de elementos pequeños, avisos |
| `Motion.Standard` | 240 ms | `CubicEaseOut` | Cambio de página, velo, cajón, expansores |
| `Motion.Emphasized` | 320 ms | `SplineEasing 0.2,0,0,1` | Hojas inferiores, desplazamientos grandes |

Reglas:

- La salida dura ~70 % de la entrada y usa `CubicEaseIn` (lo que se va no debe hacer esperar).
- Listas que aparecen: escalonado de 30 ms por elemento, máximo 6 elementos animados.
- Nada lineal salvo barras de progreso indeterminadas y spinners.
- Respetar la preferencia del sistema de reducir animaciones (Android: "Quitar animaciones")
  y ofrecer el mismo interruptor en Ajustes: con él activo, todo se reduce a fundidos de 100 ms.

### 3.6 Iconos

- **Un único set vectorial** (recomendado: Lucide, licencia ISC) como `StreamGeometry` en
  `Themes/Icons.axaml`, usado con `PathIcon`. Claves `Icon.Refresh`, `Icon.Menu`, `Icon.Close`…
- Tamaños: 16 (en línea con texto), 20 (botones e ítems de lista), 24 (barra superior e inferior).
- **Prohibidos los caracteres Unicode como iconos** (☰ ↻ › ✕ ✓ ▤ ⌄ ⌃ ← ↑): se dibujan con la
  fuente de respaldo de cada dispositivo, con grosor y alineación distintos.
- El color del icono sigue al texto que acompaña, salvo iconos de estado (semánticos).

---

## 4. Componentes

### 4.1 Botones

| Variante | Clase | Cuándo |
|---|---|---|
| Principal | `btn-primary` | La acción principal de la pantalla u hoja. **Una sola visible.** |
| Secundario | `btn-secondary` | Acciones alternativas. |
| Fantasma | `btn-ghost` | Acciones terciarias, "Cancelar", enlaces dentro de tarjetas. |
| Peligro | `btn-danger` | Confirmación de una acción destructiva (solo dentro del diálogo de confirmación). |
| Icono | `btn-icon` | Acción con icono solo; requiere `AutomationProperties.Name` y `ToolTip.Tip`. |

- Altura mínima: **48** en móvil, 36 en escritorio. Área táctil mínima 48×48 aunque el icono sea
  más pequeño.
- Estados obligatorios: normal, hover (escritorio), pulsado (`scale(0.97)`, `Motion.Instant`),
  deshabilitado (opacidad 0,4), ocupado (spinner en lugar del texto, mismo ancho).
- El texto es un verbo concreto: "Restaurar base de datos", "Eliminar copia". Nunca "Aceptar",
  "OK" ni "Sí" para una acción con consecuencias.

### 4.2 Tarjetas y listas

- Tarjeta: `Surface.Card`, borde `Border.Subtle`, `Radius.L`, relleno según §3.3.
- Fila de lista móvil: altura mínima 56 (una línea) o 72 (dos líneas); estructura
  `[icono/estado] [título + subtítulo] [valor o chevron]`.
- Una lista larga **no** va dentro de un `ScrollViewer`: se desplaza con el propio `ListBox`
  para conservar la virtualización.

### 4.3 Hojas inferiores (móvil)

- Única implementación compartida (`Mobile.axaml` + `MobileSheet`). Ninguna vista redefine
  `Border.sheet` o `Border.scrim` con otros valores.
- Asa de arrastre (36×4, `Radius.Pill`) arriba; se cierra con: arrastre hacia abajo, toque en el
  velo y botón atrás.
- Para: acciones de un elemento, formularios cortos, selección de opciones, confirmaciones.

### 4.4 Diálogos

- En escritorio: centrados, ancho 420–560. En móvil: como hoja inferior.
- Estructura: título (pregunta o acción), consecuencia en una o dos frases, acciones a la
  derecha (escritorio) o apiladas con la principal abajo (móvil).

### 4.5 Avisos (toasts)

- Éxito e información: 3 s. Aviso: 5 s. Error: hasta que se cierre o 8 s si incluye acción.
- Máximo uno visible; los siguientes se encolan.
- Cuando la acción es reversible, el aviso ofrece "Deshacer" en vez de pedir confirmación antes.

### 4.6 Formularios

- Etiqueta encima del campo (`t-label`), ayuda o error debajo (`t-body-s`).
- Validar al salir del campo, no en cada tecla. El error dice cómo arreglarlo.
- Valores de ejemplo como `PlaceholderText`, nunca como texto inicial del campo.
- Opciones cerradas con 2–5 valores: botones segmentados o radio. Más de 5: combo o hoja con lista.

### 4.7 Estados de una vista remota

Toda vista que consulta el VPS implementa los cinco estados. Ninguno puede quedar como un
texto suelto en la parte superior.

| Estado | Qué se ve |
|---|---|
| Carga inicial (> 300 ms) | Esqueleto con la forma del contenido final y pulso de opacidad. Antes de 300 ms, nada (evita el parpadeo). |
| Refresco con datos | Se mantienen los datos; indicador discreto (barra fina arriba o el del gesto de refresco). |
| Vacío | Icono 48 px, título, una frase y la acción que lo resuelve ("Aún no hay copias · Crear copia"). |
| Error | Qué pasó, por qué (usando `UserFacingErrors`) y "Reintentar". Distinguir sin conexión, autenticación, permisos, capacidad ausente y comando fallido. |
| Contenido | La vista normal. |

---

## 5. Patrones móviles (Android)

| Patrón | Norma |
|---|---|
| Navegación | Barra inferior con 4 destinos frecuentes + "Más" (ver UX_AUDIT §5, fase 2). El cajón lateral queda para "Más". |
| Botón atrás | Siempre manejado (`TopLevel.BackRequested`), en este orden: cerrar hoja/diálogo → cerrar cajón → volver a la pantalla anterior → ir al Panel → salir. |
| Refrescar | Arrastrar hacia abajo (`RefreshContainer`) en toda lista o panel remoto. El botón ↻ de la barra superior desaparece. |
| Acciones de elemento | Toque = detalle u hoja de acciones. Deslizar = acción rápida no destructiva (p. ej. reiniciar contenedor). Mantener pulsado = selección múltiple, si aplica. |
| Vibración | Corta al confirmar una acción, distinta al fallar, al llegar al umbral del gesto de refresco. Detrás de una interfaz (`IHapticFeedback`) implementada en `VpsDesk.Android`. |
| Barras del sistema | Barra de estado y de navegación con el color de `Surface.Bar`; contenido respetando las zonas seguras (`targetSdkVersion` 36 implica edge-to-edge en Android 15+). |
| Arranque | `windowBackground` y fondo del splash = `Color.Canvas`, sin destello blanco. |
| Teclado | El contenido se encoge (ya implementado en `MobileShell`); el campo activo y su botón quedan visibles. |
| Alcance con una mano | La acción principal va abajo (botón ancho al pie de la hoja o pantalla), no en la barra superior. |

## 6. Patrones de escritorio

- Atajos: `F5` refrescar, `Ctrl+1…9` navegar, `Ctrl+K` paleta de comandos, `Esc` cerrar diálogo.
- Hover visible en todo lo que se pueda pulsar; foco de teclado visible (anillo de 2 px de
  `Color.Accent.Text`).
- Clic derecho = mismas acciones que la hoja de acciones del móvil.
- Todo botón de icono lleva `ToolTip.Tip`.

---

## 7. Contenido y localización

- **Ningún texto visible escrito a mano** en XAML ni en ViewModels. XAML usa
  `{DynamicResource Clave}`; C# usa `LocalizationService.T` / `Format`. Ver
  [LOCALIZATION.md](LOCALIZATION.md). Excepción: nombres propios ("VPS Desk", "Docker").
- Tuteo neutro, frases cortas, sin jerga innecesaria. Términos técnicos (contenedor, volumen,
  commit) se mantienen.
- Los errores dicen qué pasó y qué hacer: "No hay conexión con el VPS. Revisa la red y vuelve a
  intentarlo", no "No se pudo completar la operación".
- Fechas `dd/MM/yyyy HH:mm` en hora local; tamaños con `BackupSizeConverter`; nunca mostrar
  desfases `+00:00` ni bytes crudos.

## 8. Accesibilidad

- Contraste según §3.1.
- `AutomationProperties.Name` en todo control sin texto visible (TalkBack lo lee).
- Objetivos táctiles ≥ 48×48 en móvil.
- Ningún estado comunicado solo por color.
- Probar con el tamaño de letra del sistema en "Grande": nada se corta ni se superpone.

## 9. Acciones peligrosas

| Nivel | Ejemplos | Protección |
|---|---|---|
| 1 · Reversible | Reiniciar contenedor, refrescar, descargar | Sin confirmación. Aviso con "Deshacer" si aplica. |
| 2 · Destructiva acotada | Detener contenedor, borrar archivo o copia | Diálogo que nombra el objeto y el servidor; botón `btn-danger` con el verbo. |
| 3 · Irreversible o de alcance amplio | Restaurar BD o VPS, `prune`, `down`, borrar volumen | Frase escrita (`RESTAURAR`), nombre del servidor y su entorno visibles, opción de copia de seguridad previa activada por defecto. |

En entornos marcados como producción, el distintivo **PROD** es visible de forma permanente en
la barra superior.

## 10. Rendimiento percibido

- Ningún comando SSH en el hilo de UI.
- 60 fps en desplazamiento y animaciones en un teléfono de gama media.
- Listas largas virtualizadas (§4.2).
- Interfaz optimista cuando el resultado es casi seguro (marcar como "reiniciando" al instante y
  corregir si falla).
- Actualizar propiedades existentes en lugar de recrear colecciones completas.

---

## 11. Lista de verificación para un PR que toca UI

- [ ] Sin colores hexadecimales, tamaños de letra, radios ni duraciones escritos a mano en `Views/`
      (solo tokens de `Themes/`).
- [ ] Textos nuevos en `es-ES` y `en-US`; `./Scripts/Validate-Localization.ps1` pasa.
- [ ] Botones de icono con `AutomationProperties.Name` y `ToolTip.Tip`.
- [ ] Objetivos táctiles ≥ 48 en las vistas móviles.
- [ ] Estados de carga, vacío y error implementados (§4.7).
- [ ] Una sola acción principal por pantalla u hoja.
- [ ] Acción destructiva protegida según su nivel (§9).
- [ ] En Android: botón atrás probado, teclado probado, tema oscuro y claro revisados.
- [ ] Captura de pantalla de móvil y escritorio adjunta al PR.
