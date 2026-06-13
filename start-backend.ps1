# WinEventMonitor - Arrancar servicio backend
# Haz doble clic → "Ejecutar con PowerShell" (o clic derecho → Ejecutar como administrador)

$ErrorActionPreference = "Stop"

# Verificar que somos admin
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: Este script necesita ejecutarse como Administrador." -ForegroundColor Red
    Write-Host "Haz clic derecho en el fichero y selecciona 'Ejecutar como administrador'" -ForegroundColor Yellow
    Read-Host "Pulsa Enter para salir"
    exit 1
}

$serviceDir = Join-Path $PSScriptRoot "WinEventMonitor.Service"
$dllPath    = Join-Path $serviceDir "bin\Debug\net9.0-windows\WinEventMonitor.Service.dll"

Write-Host ""
Write-Host "=== WinEventMonitor Backend ===" -ForegroundColor Cyan
Write-Host ""

# Si el DLL ya existe lo lanzamos directamente (más rápido que dotnet run)
if (Test-Path $dllPath) {
    Write-Host "Lanzando binario compilado..." -ForegroundColor Green
    Push-Location $serviceDir
    dotnet $dllPath
    Pop-Location
} else {
    # Primera vez: compilar y ejecutar
    Write-Host "Primera ejecución: compilando..." -ForegroundColor Yellow
    Push-Location $serviceDir
    dotnet run --configuration Debug
    Pop-Location
}
