@echo off
rem Lanzador de android-deploy.ps1. Doble clic abre el menu; tambien acepta parametros:
rem   android-deploy.cmd -Doctor
rem   android-deploy.cmd -All
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0android-deploy.ps1" %*
echo.
pause
