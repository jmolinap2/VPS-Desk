# Primera prueba funcional de VPS Desk v2

Esta prueba valida únicamente la base de conexión y monitoreo. No ejecuta deploys, reinicios, limpieza, eliminación de contenedores ni otras acciones destructivas.

## Alcance que debe funcionar

1. La aplicación inicia como escritorio Avalonia.
2. `Servers` permite crear y editar un perfil de VPS.
3. `Test connection` valida SSH y detecta capacidades Linux.
4. `Use this server` activa el perfil.
5. `Dashboard` consulta CPU, RAM, disco, red, uptime y load average.
6. Después de una lectura correcta, el Dashboard refresca la telemetría cada 10 segundos.
7. CPU y disco se muestran con gauges radiales; CPU/RAM mantienen una gráfica temporal.
8. Docker y Nginx muestran disponibilidad/versión cuando se detectan.
9. Los datos no sensibles del perfil persisten localmente.
10. La contraseña o passphrase no se persiste entre sesiones.

## Configuración recomendada para Hostinger

- Provider: `Hostinger`
- Host / IP: IP pública o DNS del VPS.
- SSH port: normalmente `22`, salvo que se haya cambiado.
- Username: el usuario real con el que ya se puede entrar por SSH.
- Authentication: preferir `PrivateKey`.
- Private key path: ruta local de la llave privada.
- Password / key passphrase: solo si corresponde; se mantiene únicamente en memoria.
- SSH host fingerprint SHA256: recomendado. Debe compararse con una fuente confiable antes de guardarlo.

El fingerprint puede escribirse como `SHA256:abc...` o solamente `abc...`.

## Resultado esperado de Test connection

Un resultado correcto debe indicar:

- conexión SSH válida;
- distribución Linux detectada;
- nivel de compatibilidad;
- capacidades disponibles, por ejemplo Bash, systemd, journalctl, Docker, Docker Compose, Git o Nginx.

Que una capacidad opcional no exista no debe impedir usar el Dashboard base.

## Resultado esperado del Dashboard

- `SSH Online`.
- CPU entre 0 y 100% basada en `/proc/stat`.
- RAM usada/total basada en `MemAvailable`.
- disco raíz usado/total.
- RX/TX aproximado por segundo.
- uptime.
- load average 1/5/15 separado del porcentaje de CPU.
- actualización automática luego de la primera lectura exitosa.

## Qué reportar si falla

Anotar:

- en qué paso ocurrió;
- texto exacto mostrado por VPS Desk;
- si la misma llave/usuario funciona con `ssh` normal;
- distribución del VPS (`Ubuntu 24.04`, Debian, etc.);
- si Docker/Compose están instalados.

No compartir contraseñas, llaves privadas, tokens ni archivos `.env` reales.

## Fuera de esta primera prueba

Aún no se considera terminada la funcionalidad de Containers, Deployments, Logs, Storage, Files, Terminal, Security ni Settings. Esas áreas se habilitarán sobre esta base cuando la conexión y la telemetría hayan sido validadas contra el VPS real.
