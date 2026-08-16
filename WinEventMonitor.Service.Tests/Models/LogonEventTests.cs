using WinEventMonitor.Service.Models;
using Xunit;

namespace WinEventMonitor.Service.Tests.Models;

public class LogonEventTests
{
    [Theory]
    [InlineData(2, "Interactive")]
    [InlineData(3, "Network")]
    [InlineData(4, "Batch")]
    [InlineData(5, "Service")]
    [InlineData(7, "Unlock")]
    [InlineData(8, "NetworkCleartext")]
    [InlineData(9, "NewCredentials")]
    [InlineData(10, "RemoteInteractive (RDP)")]
    [InlineData(11, "CachedInteractive")]
    public void GetLogonTypeName_MapsKnownTypes(int logonType, string expected)
    {
        Assert.Equal(expected, LogonEvent.GetLogonTypeName(logonType));
    }

    [Fact]
    public void GetLogonTypeName_FallsBackForUnknownTypes()
    {
        Assert.Equal("Type 99", LogonEvent.GetLogonTypeName(99));
    }
}
