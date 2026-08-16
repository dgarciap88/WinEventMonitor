using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WinEventMonitor.Service.Security;
using Xunit;

namespace WinEventMonitor.Service.Tests.Security;

public class ApiKeyServiceTests : IDisposable
{
    private readonly string _keyFilePath;
    private readonly ApiKeyService _sut;

    public ApiKeyServiceTests()
    {
        _keyFilePath = Path.Combine(Path.GetTempPath(), $"wem-apikey-test-{Guid.NewGuid():N}.key");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventMonitor:ApiKeyPath"] = _keyFilePath,
            })
            .Build();
        _sut = new ApiKeyService(config, NullLogger<ApiKeyService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath);
    }

    [Fact]
    public void GetOrCreateKey_CreatesFileOnFirstCall()
    {
        Assert.False(File.Exists(_keyFilePath));
        _sut.GetOrCreateKey();
        Assert.True(File.Exists(_keyFilePath));
    }

    [Fact]
    public void GetOrCreateKey_IsIdempotentAcrossInstances()
    {
        var first = _sut.GetOrCreateKey();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventMonitor:ApiKeyPath"] = _keyFilePath,
            })
            .Build();
        var second = new ApiKeyService(config, NullLogger<ApiKeyService>.Instance);

        Assert.Equal(first, second.GetOrCreateKey());
    }

    [Fact]
    public void GetOrCreateKey_ProducesA64CharHexKey()
    {
        var key = _sut.GetOrCreateKey();

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void Validate_AcceptsTheCorrectKey()
    {
        var key = _sut.GetOrCreateKey();
        Assert.True(_sut.Validate(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key-entirely")]
    public void Validate_RejectsMissingOrWrongKeys(string? provided)
    {
        _sut.GetOrCreateKey();
        Assert.False(_sut.Validate(provided));
    }

    [Fact]
    public void Validate_RejectsAKeyOfDifferentLength()
    {
        var key = _sut.GetOrCreateKey();
        Assert.False(_sut.Validate(key + "extra"));
        Assert.False(_sut.Validate(key[..^1]));
    }
}
