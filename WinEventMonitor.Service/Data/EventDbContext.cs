using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Data;

public class EventDbContext : DbContext
{
    public EventDbContext(DbContextOptions<EventDbContext> options) : base(options) { }

    public DbSet<ProcessEvent> ProcessEvents => Set<ProcessEvent>();
    public DbSet<NetworkEvent> NetworkEvents => Set<NetworkEvent>();
    public DbSet<DnsEvent> DnsEvents => Set<DnsEvent>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<LogonEvent> LogonEvents => Set<LogonEvent>();
    public DbSet<SysmonAdvancedEvent> SysmonAdvancedEvents => Set<SysmonAdvancedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessEvent>(e =>
        {
            e.HasIndex(p => p.Timestamp);
            e.HasIndex(p => p.ProcessName);
            e.HasIndex(p => p.IsElevated);
        });

        modelBuilder.Entity<NetworkEvent>(e =>
        {
            e.HasIndex(n => n.Timestamp);
            e.HasIndex(n => n.ProcessName);
            e.HasIndex(n => n.DestinationIp);
        });

        modelBuilder.Entity<DnsEvent>(e =>
        {
            e.HasIndex(d => d.Timestamp);
            e.HasIndex(d => d.QueryName);
            e.HasIndex(d => d.ProcessName);
        });

        modelBuilder.Entity<AlertEvent>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.Severity);
        });

        modelBuilder.Entity<LogonEvent>(e =>
        {
            e.HasIndex(l => l.Timestamp);
            e.HasIndex(l => l.Success);
            e.HasIndex(l => l.LogonType);
            e.HasIndex(l => l.SourceIp);
        });

        modelBuilder.Entity<SysmonAdvancedEvent>(e =>
        {
            e.HasIndex(s => s.Timestamp);
            e.HasIndex(s => s.EventId);
            e.HasIndex(s => s.SourcePid);
            e.HasIndex(s => s.TargetPid);
        });
    }
}
