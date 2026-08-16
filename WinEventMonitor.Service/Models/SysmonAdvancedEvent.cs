namespace WinEventMonitor.Service.Models;

/// <summary>
/// Eventos Sysmon avanzados:
///   ID 7  – Image Load (DLL / módulo cargado en un proceso)
///   ID 8  – CreateRemoteThread (inyección de hilo en proceso externo)
///   ID 10 – ProcessAccess (apertura de proceso ajeno con permisos de lectura, típico en credential dump)
///   ID 13 – RegistryEvent (valor de registro modificado, usado para detectar persistencia)
///
/// Un único modelo evita la proliferación de tablas para estos tipos relacionados.
/// </summary>
public class SysmonAdvancedEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int EventId { get; set; }              // 7, 8, 10 o 13

    // ── Proceso origen ─────────────────────────────────────────────────────
    public int SourcePid { get; set; }
    public string SourceProcessName { get; set; } = string.Empty;

    // ── ID 7: imagen cargada (DLL / módulo) ────────────────────────────────
    public string? ImagePath { get; set; }
    public bool? Signed { get; set; }
    public string? Signature { get; set; }
    public string? SignatureStatus { get; set; }  // Valid | Revoked | Unsigned | …
    public string? Sha256 { get; set; }

    // ── ID 8 / ID 10: proceso destino ──────────────────────────────────────
    public int? TargetPid { get; set; }
    public string? TargetProcessName { get; set; }

    // ── ID 8: CreateRemoteThread ───────────────────────────────────────────
    public string? StartAddress { get; set; }
    public string? StartModule { get; set; }
    public string? StartFunction { get; set; }

    // ── ID 10: ProcessAccess ──────────────────────────────────────────────
    public string? GrantedAccess { get; set; }
    public string? CallTrace { get; set; }

    // ── ID 13: RegistryEvent (valor de registro modificado) ───────────────
    public string? TargetObject { get; set; }      // ruta de la clave\valor de registro
    public string? RegistryDetails { get; set; }    // valor escrito
}
