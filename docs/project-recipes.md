# Recetas de proyecto de VPS Desk

VPS Desk funciona sin archivos especiales: detecta proyectos Git con Docker Compose, sus archivos Compose y sus servicios. Una receta `.vpsdesk.yml` o `.vpsdesk.yaml` es **opcional** y permite que un proyecto describa una experiencia de despliegue más rica sin acoplar VPS Desk a un framework, proveedor, arquitectura SaaS o concepto de tenant concreto.

## Principio

**VPS Desk aporta el motor; el proyecto describe su receta.**

Sin receta, el operador puede desplegar todos los servicios Compose o un servicio individual. Con receta, el proyecto puede declarar objetivos con nombres amigables, por ejemplo `Completo`, `Backend` o `Frontend`, y puede declarar una operación de migraciones con los modos que necesite.

VPS Desk no interpreta palabras de dominio como `tenant`, `host`, `schema` o `cliente`. Si un modo necesita un dato adicional, la receta declara un `input` y VPS Desk renderiza el campo.

## Ubicación

La receta se guarda en la raíz del repositorio remoto:

```text
/root/mi-aplicacion/.vpsdesk.yml
```

VPS Desk también acepta `.vpsdesk.yaml`.

## Ejemplo

```yaml
version: 1

project:
  name: MiAplicacion

deploy:
  defaultTarget: full
  targets:
    - id: full
      label: Completo
      services: [api, front, worker]
      migrationsDefault: true

    - id: backend
      label: Backend
      services: [api]
      migrationsDefault: true

    - id: frontend
      label: Frontend
      services: [front]
      migrationsDefault: false

migrations:
  label: Migraciones de base de datos
  defaultMode: all
  requiredServices: [postgres, migrator]
  modes:
    - id: all
      label: Todas
      command: docker compose --profile tools -f {{compose}} run --rm migrator -q

    - id: one
      label: Una base concreta
      command: ./scripts/migrate-one.sh {{input}}
      input:
        label: Base
        placeholder: identificador
        required: true
```

## Despliegues genéricos

Si no existe receta, VPS Desk obtiene los servicios con `docker compose config --services` y ofrece:

- todos los servicios activos del Compose;
- un objetivo por cada servicio detectado.

No se habilitan migraciones automáticamente porque VPS Desk no puede deducir de forma segura cómo debe migrarse una aplicación arbitraria.

## Targets

`deploy.targets[].services` contiene nombres reales de servicios del archivo Compose seleccionado. El prevuelo comprueba que existan antes de ejecutar cambios.

`migrationsDefault` solo define el valor inicial de la casilla de migraciones cuando ese target se selecciona. El operador puede cambiarlo antes de ejecutar.

## Migraciones

La sección `migrations` es opcional. Si no existe, VPS Desk no muestra `Solo migraciones` ni opciones de migración para ese proyecto.

`requiredServices` declara servicios Compose que deben existir para considerar válida la operación. Los servicios que pertenecen a perfiles también se validan usando todos los perfiles del Compose.

Cada modo declara su propio comando. VPS Desk lo ejecuta desde la raíz del repositorio remoto después de la actualización Git y, si corresponde, después del despliegue de los servicios seleccionados.

### Parámetros

Un modo puede declarar un único `input` contextual. VPS Desk no interpreta su significado y lo presenta con el `label` y `placeholder` definidos por el proyecto.

Los valores introducidos por el operador se citan antes de insertarse en el shell remoto.

## Tokens soportados

Los comandos pueden usar únicamente:

- `{{compose}}`: archivo Compose seleccionado;
- `{{repository}}`: ruta remota del proyecto;
- `{{branch}}`: rama seleccionada;
- `{{input}}`: valor del `input` del modo.

Los tokens dinámicos se renderizan con quoting de shell. Si queda un token `{{...}}` desconocido, VPS Desk bloquea la operación.

## Seguridad y confianza

Una receta pertenece al código del proyecto y puede declarar un comando de migración. Por tanto debe tratarse como **configuración ejecutable del repositorio**, del mismo modo que un Dockerfile, un Compose o un script de despliegue.

VPS Desk aplica estas barreras:

- el archivo se limita a 128 KB;
- se valida la versión y la estructura antes de usarlo;
- IDs y nombres de servicios tienen un formato restringido;
- el prevuelo comprueba los servicios Compose requeridos;
- los valores introducidos por el operador se escapan para shell;
- tokens desconocidos bloquean la ejecución;
- la ejecución sigue requiriendo la confirmación explícita del operador;
- la salida se sanitiza antes de mostrarse o almacenarse en el historial.

No deben colocarse contraseñas, tokens ni secretos dentro de `.vpsdesk.yml`. Los secretos pertenecen al `.env`, al almacenamiento seguro local o a los mecanismos de autenticación correspondientes.

## Flujo

Para un despliegue normal:

```text
Git fetch/checkout/pull
→ pull opcional de servicios seleccionados
→ docker compose up de servicios seleccionados
→ operación de migración opcional
→ docker compose ps
→ limpieza opcional de caché
→ postvuelo limitado al target desplegado
```

Para `Solo migraciones`:

```text
Git fetch/checkout/pull
→ operación de migración
→ docker compose ps
```

No se reconstruye ni recrea la aplicación y no se ejecuta el postvuelo de servicios de aplicación.

## Esquema

El repositorio incluye `Schemas/vpsdesk-recipe.schema.json` como referencia formal del formato versión 1.
