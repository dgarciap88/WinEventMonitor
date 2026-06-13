# =============================================================================
# build.ps1  —  Genera el instalador de WinEventMonitor
# Uso:  .\build.ps1            (build + empaquetado)
#       .\build.ps1 -SkipInno  (solo build, sin Inno Setup)
# =============================================================================
param(
    [switch]$SkipInno
)

$ErrorActionPreference = 'Stop'
$Root    = $PSScriptRoot
$WebDir  = Join-Path $Root 'WinEventMonitor.Web'
$SvcDir  = Join-Path $Root 'WinEventMonitor.Service'
$TrayDir = Join-Path $Root 'WinEventMonitor.Tray'
$Publish = Join-Path $Root 'publish'
$Version = '1.0.0'

function Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

# --- 1. Limpiar publish anterior ---
Step "Limpiando carpeta publish/"
if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
New-Item $Publish -ItemType Directory | Out-Null

# --- 2. Build del frontend (Vite → wwwroot/) ---
Step "Compilando frontend React/Vite..."
Push-Location $WebDir
npm run build
if ($LASTEXITCODE -ne 0) { throw "Error al compilar el frontend." }
Pop-Location

# --- 3. Restore + Publish del backend self-contained ---
Step "Publicando backend .NET 9 (self-contained win-x64)..."
dotnet publish "$SvcDir" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o "$Publish" `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Error al publicar el backend." }

# --- 4. Publish de la app Tray (comparte runtime con el backend) ---
Step "Publicando app Tray WPF+WebView2..."
dotnet publish "$TrayDir" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o "$Publish" `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Error al publicar la app Tray." }

# --- 5. Verificar que wwwroot fue incluido ---
$wwwroot = Join-Path $Publish 'wwwroot'
if (-not (Test-Path $wwwroot)) {
    throw "No se encontro wwwroot en publish/. Asegurate de haber ejecutado 'npm run build' antes."
}
Write-Host "  wwwroot copiado: $(Get-ChildItem $wwwroot -Recurse | Measure-Object).Count archivos" -ForegroundColor Gray

# --- 6. Inno Setup ---
if (-not $SkipInno) {
    Step "Compilando instalador con Inno Setup..."

    # Busca Inno Setup en rutas comunes (instalacion global y por usuario)
    $iscc = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        Write-Host "  AVISO: Inno Setup no encontrado. Instala desde https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host "  La carpeta publish/ esta lista para uso manual." -ForegroundColor Yellow
    } else {
        $issFile = Join-Path $Root 'installer\WinEventMonitor.iss'
        & $iscc $issFile "/DMyAppVersion=$Version" "/DPublishDir=$Publish"
        if ($LASTEXITCODE -ne 0) { throw "Error al compilar el instalador." }
        Write-Host "  Instalador generado en installer\Output\WinEventMonitor-$Version-Setup.exe" -ForegroundColor Green
    }
}

Step "Completado"
Write-Host "  Publicacion en: $Publish" -ForegroundColor Green
