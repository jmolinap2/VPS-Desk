# Paquete para Microsoft Store

`Build-Store-Package.ps1` publica VPS Desk como un paquete MSIX limpio para Microsoft Store.
No incorpora `.env`, la llave SSH, la carpeta `secrets`, perfiles, historial ni ningún dato del usuario.

## Datos requeridos

En Partner Center, crea o asocia la aplicación y copia estos valores de la identidad del paquete:

- `Package/Identity/Name` como `StoreIdentityName`.
- `Package/Identity/Publisher` como `StorePublisher`.
- El nombre público del editor como `PublisherDisplayName`.

## Generación

```powershell
./Scripts/Build-Store-Package.ps1 `
  -StoreIdentityName "IDENTIDAD_DE_PARTNER_CENTER" `
  -StorePublisher "CN=PUBLICADOR_DE_PARTNER_CENTER" `
  -PublisherDisplayName "Nombre del editor" `
  -Version "1.0.0.0"
```

El resultado se crea bajo `app/clinical-care/appointments/store/` y está ignorado por Git.

Firma local opcional:

```powershell
-CertificatePath "C:\ruta\certificado.pfx" `
-CertificatePassword (Read-Host -AsSecureString)
```
