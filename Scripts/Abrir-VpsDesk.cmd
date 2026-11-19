@echo off
rem Compila VPS Desk (escritorio) con los cambios actuales del repo y lo abre.
rem Cierra antes la instancia abierta para que la compilacion pueda reemplazar los archivos.
setlocal
set "REPO=%~dp0.."
set "PROJ=%REPO%\src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
set "OUT=%REPO%\src\VpsDesk.Desktop\bin\Release\net10.0"

title VPS Desk - compilando
taskkill /IM VpsDesk.Desktop.exe /F >nul 2>&1

echo Compilando VPS Desk...
dotnet build "%PROJ%" -c Release -v q --nologo
if errorlevel 1 (
  echo.
  echo *** La compilacion fallo. Revisa los errores de arriba. ***
  pause
  exit /b 1
)

if not exist "%OUT%\VpsDesk.Desktop.exe" (
  echo No se encontro "%OUT%\VpsDesk.Desktop.exe".
  pause
  exit /b 1
)

start "" "%OUT%\VpsDesk.Desktop.exe"
endlocal
