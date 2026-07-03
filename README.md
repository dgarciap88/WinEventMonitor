# Windows Event Monitor

Herramienta de monitorización de eventos de seguridad de Windows en tiempo real.

Captura, almacena y analiza eventos del sistema operativo — procesos, red, DNS, logons y eventos de Sysmon — y genera alertas automáticas con mapeo MITRE ATT\&CK. Se instala como servicio de Windows y expone una interfaz web accesible desde la bandeja del sistema.

---

## Características principales

- **Captura de eventos** vía Windows Event Log (Security) y Sysmon
- **16 reglas de detección** automática con etiquetas MITRE ATT\&CK (T1059, T1055, T1003, etc.)
- **Árbol de procesos** live + histórico con heurísticas de sospecha
- **Alertas** persistidas en SQLite con severidad High/Medium/Low
- **Accesos remotos**: logons 4624/4625, sesiones RDP, brute force, top atacantes
- **Timeline por proceso**: todos los eventos de un PID en un panel lateral
- **Búsqueda global** Ctrl+K sobre procesos, red, DNS y alertas
- **VirusTotal lookup** de hashes SHA256
- **Bandeja del sistema**: app nativa WPF con WebView2
- **Instalador** generado con Inno Setup 6, upgrade in-place sin perder datos

---

## Tecnologías

| Capa | Tecnología |
|------|------------|
| Backend | .NET 9 / ASP.NET Core (Worker Service) |
| Base de datos | SQLite + EF Core 9 (migraciones automáticas) |
| Frontend | React 18 + TypeScript + Vite + Tailwind CSS |
| App nativa | WPF + WebView2 (.NET 9) |
| Instalador | Inno Setup 6 |

---

## Requisitos

- Windows 10/11 x64
- .NET 9 SDK (solo para desarrollo; el instalador incluye el runtime)
- Node.js 20+ (solo para desarrollo)
- [Sysmon](https://learn.microsoft.com/sysinternals/downloads/sysmon) (opcional pero muy recomendado)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (solo para generar el instalador)

---

## Desarrollo

```powershell
# Backend (requiere admin para leer el Event Log)
cd WinEventMonitor.Service
dotnet run

# Frontend (en otra terminal)
cd WinEventMonitor.Web
npm install
npm run dev        # → http://localhost:5173
```

La API escucha en `http://localhost:51847` (configurable en `appsettings.json → EventMonitor.Port`).

La API Key se genera automáticamente en `C:\ProgramData\WinEventMonitor\api.key` al primer arranque.  
En dev, cópiala en `WinEventMonitor.Web/.env.local`:

```
VITE_API_KEY=<contenido de api.key>
VITE_API_URL=http://localhost:51847
```

---

## Estructura del repositorio

```
WinEventMonitor/
├── WinEventMonitor.Service/    ← Backend .NET 9 (servicio Windows + API REST)
│   ├── Api/                    ← Endpoints (procesos, alertas, red, logons, etc.)
│   ├── Models/                 ← Entidades SQLite
│   ├── Parsers/                ← Parsers de eventos (Sysmon IDs 1/3/5/7/8/10/22)
│   ├── Workers/                ← Workers en background (ingesta, alertas, retención)
│   ├── Services/               ← Servicios singleton (SystemHealth, AlertRules, etc.)
│   └── appsettings.json        ← Configuración (puerto, rutas, retención, Sysmon)
├── WinEventMonitor.Tray/       ← App WPF: bandeja del sistema + ventana WebView2
├── WinEventMonitor.Web/        ← Frontend React/Vite/Tailwind
├── installer/
│   └── WinEventMonitor.iss     ← Script Inno Setup
└── build.ps1                   ← Script de build + empaquetado
```

---

## Generar el instalador

```powershell
.\build.ps1
# → installer/Output/WinEventMonitor-1.0.0-Setup.exe
```

El script ejecuta en orden:
1. `npm run build` → compila el frontend a `WinEventMonitor.Service/wwwroot/`
2. `dotnet publish` → publica el backend self-contained (win-x64, sin SDK)
3. `dotnet publish` → publica la app Tray
4. `ISCC.exe` → genera el instalador `.exe`

---

## Instalación

Ejecuta `WinEventMonitor-X.Y.Z-Setup.exe` como administrador. El instalador:

- Copia los ficheros a `C:\Program Files\WinEventMonitor\`
- Registra el servicio de Windows (`sc create`, inicio automático)
- Añade la app Tray al inicio de sesión (`HKCU\...\Run`)
- Crea accesos directos en el menú inicio y (opcional) escritorio

Los datos persisten en `C:\ProgramData\WinEventMonitor\` y **nunca se eliminan** al actualizar.

---

## Configuración

Edita `appsettings.json` (en la carpeta de instalación o en el proyecto):

```json
{
  "EventMonitor": {
    "Port": 51847,
    "RetentionDays": 30,
    "Sources": {
      "Security": true,
      "Sysmon": true
    },
    "VirusTotalApiKey": ""
  }
}
```

| Campo | Descripción |
|-------|-------------|
| `Port` | Puerto de escucha (solo loopback 127.0.0.1) |
| `RetentionDays` | Días de retención de eventos (0 = infinito) |
| `VirusTotalApiKey` | API key de VirusTotal para lookup de hashes |

---

## Seguridad

- El backend escucha **exclusivamente en `127.0.0.1`** — no es accesible desde la red
- Autenticación por **API Key** autogenerada (UUID v4) en cada instalación
- La clave se almacena en `C:\ProgramData\WinEventMonitor\api.key` con permisos restringidos
- La app Tray inyecta la clave automáticamente — el usuario nunca la ve

---

## Licencia

MIT — ver [LICENSE](LICENSE)
