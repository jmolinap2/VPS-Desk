# Política De Seguridad

VPS Desk es una herramienta local de escritorio que puede conectarse a servidores VPS reales y ejecutar comandos remotos. Trata la configuración y los logs como información sensible.

## Versiones Soportadas

Los fixes de seguridad se aceptan sobre la rama `main` actual.

## Reportar Una Vulnerabilidad

Reporta vulnerabilidades de forma privada al dueño del repositorio. No abras issues públicos con exploits funcionales, detalles privados de servidores, tokens, contraseñas o llaves.

Incluye:

- Una descripción corta del problema.
- Pasos para reproducirlo usando datos de ejemplo seguros.
- La función o archivo afectado.
- Una sugerencia de solución, si ya tienes una.

## Manejo De Secretos

- No subas `.env`.
- No subas llaves privadas SSH.
- No pegues tokens o contraseñas reales en issues o pull requests.
- Rota cualquier credencial que pudo haberse expuesto.
