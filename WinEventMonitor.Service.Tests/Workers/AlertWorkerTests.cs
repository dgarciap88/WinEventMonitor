using WinEventMonitor.Service.Workers;
using Xunit;

namespace WinEventMonitor.Service.Tests.Workers;

public class AlertWorkerTests
{
    [Theory]
    [InlineData("0x1010")] // PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ
    [InlineData("0x1438")] // acceso clasico de Mimikatz
    [InlineData("0x0010")] // solo PROCESS_VM_READ
    public void HasMemoryReadAccess_TrueWhenVmReadBitSet(string grantedAccess)
    {
        Assert.True(AlertWorker.HasMemoryReadAccess(grantedAccess));
    }

    [Theory]
    [InlineData("0x1000")] // PROCESS_QUERY_LIMITED_INFORMATION - consulta de monitorizacion, sin lectura de memoria
    [InlineData("0x0400")] // PROCESS_QUERY_INFORMATION
    public void HasMemoryReadAccess_FalseWhenOnlyQueryAccess(string grantedAccess)
    {
        Assert.False(AlertWorker.HasMemoryReadAccess(grantedAccess));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-hex")]
    public void HasMemoryReadAccess_DoesNotDiscardWhenDataIsMissingOrUnparseable(string? grantedAccess)
    {
        Assert.True(AlertWorker.HasMemoryReadAccess(grantedAccess));
    }

    [Theory]
    [InlineData("xqzvbnmqwrtyplk.com")]     // racha larga de consonantes
    [InlineData("a1b2c3d4e5f6g7h8.net")]    // alta densidad de digitos
    public void LooksAlgorithmicallyGenerated_TrueForGibberishLabels(string domain)
    {
        Assert.True(AlertWorker.LooksAlgorithmicallyGenerated(domain));
    }

    [Theory]
    [InlineData("generativelanguage.googleapis.com")] // dominio real largo, pero legible
    [InlineData("github.com")]
    [InlineData("xkcd.com")]                            // corto, aunque consonante-denso
    public void LooksAlgorithmicallyGenerated_FalseForRealisticDomains(string domain)
    {
        Assert.False(AlertWorker.LooksAlgorithmicallyGenerated(domain));
    }
}
