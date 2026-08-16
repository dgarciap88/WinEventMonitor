using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Services;
using Xunit;

namespace WinEventMonitor.Service.Tests.Services;

public class AlertServiceTests
{
    private static IDbContextFactory<EventDbContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventDbContext>(opt =>
            opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<EventDbContext>>();
    }

    [Fact]
    public async Task AddAsync_PersistsAlert_WhenNoException()
    {
        var factory = CreateFactory();
        var sut = new AlertService(factory, NullLogger<AlertService>.Instance);

        await sut.AddAsync(new AlertEvent { Rule = "Test Rule", ProcessName = "foo.exe" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task AddAsync_Suppresses_WhenExceptionMatchesRuleAndProcess()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.AlertExceptions.Add(new AlertException { Rule = "Test Rule", ProcessName = "foo.exe" });
            await seed.SaveChangesAsync();
        }

        var sut = new AlertService(factory, NullLogger<AlertService>.Instance);
        await sut.AddAsync(new AlertEvent { Rule = "Test Rule", ProcessName = "foo.exe" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task AddAsync_Suppresses_WhenExceptionHasNoProcessNameRestriction()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.AlertExceptions.Add(new AlertException { Rule = "Test Rule", ProcessName = null });
            await seed.SaveChangesAsync();
        }

        var sut = new AlertService(factory, NullLogger<AlertService>.Instance);
        await sut.AddAsync(new AlertEvent { Rule = "Test Rule", ProcessName = "anything.exe" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DoesNotSuppress_WhenExceptionIsForADifferentProcess()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.AlertExceptions.Add(new AlertException { Rule = "Test Rule", ProcessName = "trusted.exe" });
            await seed.SaveChangesAsync();
        }

        var sut = new AlertService(factory, NullLogger<AlertService>.Instance);
        await sut.AddAsync(new AlertEvent { Rule = "Test Rule", ProcessName = "other.exe" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DoesNotSuppress_WhenExceptionIsForADifferentRule()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.AlertExceptions.Add(new AlertException { Rule = "Other Rule", ProcessName = null });
            await seed.SaveChangesAsync();
        }

        var sut = new AlertService(factory, NullLogger<AlertService>.Instance);
        await sut.AddAsync(new AlertEvent { Rule = "Test Rule", ProcessName = "foo.exe" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.AlertEvents.CountAsync());
    }
}
