using System.Text.Json;

namespace WinEventMonitor.Service.Services;

/// <summary>
/// Singleton. Mantiene la configuración de reglas de alerta en memoria
/// y persiste en un JSON para sobrevivir reinicios.
/// </summary>
public sealed class AlertRulesService
{
    private static readonly string ConfigPath =
        Path.Combine(@"C:\ProgramData\WinEventMonitor", "alert_rules.json");

    private readonly Dictionary<int, AlertRuleConfig> _rules;
    private readonly object _lock = new();

    // Definición de reglas por defecto
    private static readonly AlertRuleConfig[] Defaults =
    [
        new(1, "LOLBin – Shell desde app de documentos", "High",    true,  "Shell lanzado por Word/Excel/navegador u otra app de documentos"),
        new(2, "PowerShell Encoded",                     "High",    true,  "Ejecución de PowerShell con parámetro -EncodedCommand"),
        new(3, "Proceso desde ruta sospechosa",          "Medium",  true,  "Proceso iniciado desde Temp, Public, Downloads u otra ruta de escritura libre"),
        new(4, "DNS – TLD sospechoso",                   "Medium",  true,  "Consulta DNS a dominio con TLD de alto riesgo (.xyz, .tk, .onion…)"),
        new(5, "Movimiento lateral – Puerto de administración", "Medium", true, "Conexión saliente a puertos SMB/RDP/WinRM/SSH"),
        new(6, "Reverse Shell – Shell con conexión de red", "High", true,  "Shell (cmd, ps…) con tráfico de red activo en la misma ventana"),
        new(7, "Fuerza bruta – Logon fallido repetido",  "High",    true,  "≥5 logons fallidos desde la misma IP en 5 min"),
        new(8, "RDP desde IP nueva",                     "Medium",  true,  "Sesión RDP exitosa desde IP no vista en los últimos 7 días"),
        // ── Nuevas reglas (detección sin nuevas fuentes) ──────────────────────────
        new(9,  "Script Host – Hijo de wscript/cscript",   "High",   true, "Proceso hijo lanzado por wscript.exe o cscript.exe (posible macro maliciosa)"),
        new(10, "PowerShell Dropper",                       "High",   true, "PowerShell usando IEX, DownloadString, Invoke-Expression o Net.WebClient"),
        new(11, "Ejecución desde UNC Path",                 "High",   true, "Proceso ejecutado desde ruta de red (\\\\servidor\\recurso)"),
        new(12, "Eliminación de Shadow Copies",             "High",   true, "vssadmin/wmic/bcdedit destruyendo copias de seguridad (ransomware típico)"),
        new(13, "LOLBin – msiexec/regsvr32/wmic proxy",    "High",   true, "Shell ejecutado mediante binarios proxy de confianza del sistema"),
        // ── Sysmon avanzado (IDs 7, 8, 10) ──────────────────────────────────────
        new(14, "DLL sin firma cargada",                    "Medium", true, "Imagen no firmada cargada por un proceso (Sysmon ID 7)"),
        new(15, "CreateRemoteThread – Inyección de hilo",  "High",   true, "Hilo inyectado en proceso externo (Sysmon ID 8)"),
        new(16, "Acceso a LSASS – Volcado de credenciales","High",   true, "Proceso accediendo a lsass.exe con permisos de lectura (Sysmon ID 10)"),
    ];

    public AlertRulesService()
    {
        _rules = Defaults.ToDictionary(r => r.Id);
        Load();
    }

    // ─── Acceso ──────────────────────────────────────────────────────────────

    public IReadOnlyList<AlertRuleConfig> GetAll()
    {
        lock (_lock) return _rules.Values.OrderBy(r => r.Id).ToList();
    }

    public bool IsEnabled(int ruleId)
    {
        lock (_lock) return _rules.TryGetValue(ruleId, out var r) && r.Enabled;
    }

    public string GetSeverity(int ruleId, string fallback = "Medium")
    {
        lock (_lock) return _rules.TryGetValue(ruleId, out var r) ? r.Severity : fallback;
    }

    public bool TryUpdate(int ruleId, bool enabled, string? severity, out AlertRuleConfig updated)
    {
        lock (_lock)
        {
            if (!_rules.TryGetValue(ruleId, out var current))
            {
                updated = null!;
                return false;
            }
            var next = current with
            {
                Enabled  = enabled,
                Severity = severity ?? current.Severity,
            };
            _rules[ruleId] = next;
            updated = next;
        }
        SaveAsync(); // fire-and-forget
        return true;
    }

    // ─── Persistencia ────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json    = File.ReadAllText(ConfigPath);
            var saved   = JsonSerializer.Deserialize<AlertRuleConfig[]>(json);
            if (saved is null) return;
            lock (_lock)
            {
                foreach (var s in saved)
                    if (_rules.ContainsKey(s.Id))
                        _rules[s.Id] = _rules[s.Id] with { Enabled = s.Enabled, Severity = s.Severity };
            }
        }
        catch { /* si el JSON está corrupto simplemente usamos defaults */ }
    }

    private async void SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            IReadOnlyList<AlertRuleConfig> snapshot;
            lock (_lock) snapshot = _rules.Values.OrderBy(r => r.Id).ToList();
            var json = JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ConfigPath, json);
        }
        catch { }
    }
}

// ─── DTO ─────────────────────────────────────────────────────────────────────

public record AlertRuleConfig(
    int    Id,
    string Name,
    string Severity,
    bool   Enabled,
    string Description
);
