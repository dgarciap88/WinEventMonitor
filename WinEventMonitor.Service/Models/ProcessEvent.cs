namespace WinEventMonitor.Service.Models;

public class ProcessEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;   // "Create" | "Terminate"
    public string EventSource { get; set; } = string.Empty; // "Security" | "Sysmon"
    public int Pid { get; set; }
    public int? ParentPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? CommandLine { get; set; }
    public string? UserName { get; set; }
    public bool IsElevated { get; set; }
    public string? IntegrityLevel { get; set; }
    public string? Sha256 { get; set; }
}
