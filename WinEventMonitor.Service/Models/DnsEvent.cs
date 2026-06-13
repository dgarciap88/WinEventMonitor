namespace WinEventMonitor.Service.Models;

public class DnsEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string QueryName { get; set; } = string.Empty;
    public string? QueryResults { get; set; }  // IPs separadas por ";"
    public string? QueryStatus { get; set; }
}
