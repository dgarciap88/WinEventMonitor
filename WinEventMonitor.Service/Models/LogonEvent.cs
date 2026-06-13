namespace WinEventMonitor.Service.Models;

/// <summary>
/// Evento de inicio de sesión (4624 = éxito, 4625 = fallo).
/// LogonType: 2=Interactive, 3=Network, 4=Batch, 5=Service, 7=Unlock,
///            10=RemoteInteractive(RDP), 11=CachedInteractive
/// </summary>
public class LogonEvent
{
    public int      Id                { get; set; }
    public DateTime Timestamp         { get; set; }
    public int      EventId           { get; set; }   // 4624 | 4625
    public bool     Success           { get; set; }   // true=4624, false=4625
    public int      LogonType         { get; set; }
    public string   LogonTypeName     { get; set; } = string.Empty;
    public string?  UserName          { get; set; }
    public string?  Domain            { get; set; }
    public string?  SourceIp          { get; set; }
    public int?     SourcePort        { get; set; }
    public string?  WorkstationName   { get; set; }
    public string?  LogonProcessName  { get; set; }
    public string?  AuthPackage       { get; set; }
    public string?  FailureReason     { get; set; }   // solo en 4625

    public static string GetLogonTypeName(int t) => t switch
    {
        2  => "Interactive",
        3  => "Network",
        4  => "Batch",
        5  => "Service",
        7  => "Unlock",
        8  => "NetworkCleartext",
        9  => "NewCredentials",
        10 => "RemoteInteractive (RDP)",
        11 => "CachedInteractive",
        _ => $"Type {t}"
    };
}
