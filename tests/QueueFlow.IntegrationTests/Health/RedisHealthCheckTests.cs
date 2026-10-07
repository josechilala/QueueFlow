using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueueFlow.Api.Health;
using StackExchange.Redis;

namespace QueueFlow.IntegrationTests.Health;

public sealed class RedisHealthCheckTests
{
    [Fact]
    public async Task UsesRegisteredMultiplexerDatabasePing()
    {
        var database = DispatchProxy.Create<IDatabase, DatabaseProxy>();
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, MultiplexerProxy>();
        ((MultiplexerProxy)(object)multiplexer).Database = database;

        var result = await new RedisHealthCheck(multiplexer)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, ((DatabaseProxy)(object)database).PingCount);
    }

    [Fact]
    public async Task TreatsRedisAsHealthyWhenItIsNotConfigured()
    {
        var result = await new RedisHealthCheck().CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    public class MultiplexerProxy : DispatchProxy
    {
        public IDatabase Database { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == "GetDatabase" ? Database : null;
    }

    public class DatabaseProxy : DispatchProxy
    {
        public int PingCount { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "PingAsync") { PingCount++; return Task.FromResult(TimeSpan.Zero); }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
