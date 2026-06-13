namespace WinEventMonitor.Service.Models;

public class NetworkEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Protocol { get; set; }    // "TCP" | "UDP"
    public string? SourceIp { get; set; }
    public int? SourcePort { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public bool Initiated { get; set; }      // true = saliente
    public string? ExecutablePath { get; set; } // ruta completa del ejecutable
}
