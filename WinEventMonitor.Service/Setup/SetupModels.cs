namespace WinEventMonitor.Service.Setup;

public record SetupStatus(SysmonStatus Sysmon, AuditPolicyStatus AuditPolicy, StorageInfo Storage);

public record SysmonStatus(
    bool ExecutableFound,
    string? ExecutablePath,
    bool ServiceRunning,
    bool ConfigApplied);

public record AuditPolicyStatus(bool ProcessCreationEnabled, bool LogonAuditEnabled);

public record StorageInfo(
    long FileSizeBytes,
    string FileSizeMb,
    int RetentionDays,
    long TotalProcessEvents,
    long TotalNetworkEvents,
    long TotalDnsEvents,
    long TotalLogonEvents);
