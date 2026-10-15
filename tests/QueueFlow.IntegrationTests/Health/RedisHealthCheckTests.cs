using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
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

    [Theory]
    [InlineData("timeout")]
    [InlineData("caller-canceled")]
    [InlineData("unavailable")]
    public async Task DistinguishesTimeoutCancellationAndUnavailableWithoutLeakingMessages(string scenario)
    {
        var database = DispatchProxy.Create<IDatabase, DatabaseProxy>();
        var pending = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((DatabaseProxy)(object)database).Ping = scenario == "unavailable"
            ? Task.FromException<TimeSpan>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "private-provider-detail"))
            : pending.Task;
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, MultiplexerProxy>();
        ((MultiplexerProxy)(object)multiplexer).Database = database;
        var logs = new HealthLogCapture();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        var check = new RedisHealthCheck(multiplexer, factory.CreateLogger<RedisHealthCheck>());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var checking = check.CheckHealthAsync(new(), cancellation.Token);
        if (scenario == "caller-canceled") await cancellation.CancelAsync();
        try
        {
            if (scenario == "caller-canceled")
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checking);
                Assert.Contains(logs.Entries, x => x.Message.Contains("canceled by caller", StringComparison.Ordinal));
                Assert.DoesNotContain(logs.Entries, x => x.Level >= LogLevel.Warning);
            }
            else
            {
                var result = await checking;
                Assert.Equal(HealthStatus.Unhealthy, result.Status);
                Assert.Null(result.Exception);
                Assert.Contains(logs.Entries, x => x.Message.Contains(scenario == "timeout" ? "internal timeout" : "dependency unavailable", StringComparison.Ordinal));
            }
            Assert.DoesNotContain(logs.Entries, x => x.Message.Contains("private-provider-detail", StringComparison.Ordinal) || x.Exception is not null);
        }
        finally { pending.TrySetResult(TimeSpan.Zero); }
    }

    public class MultiplexerProxy : DispatchProxy
    {
        public IDatabase Database { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == "GetDatabase" ? Database : null;
    }

    public class DatabaseProxy : DispatchProxy
    {
        public int PingCount { get; private set; }
        public Task<TimeSpan> Ping { get; set; } = Task.FromResult(TimeSpan.Zero);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "PingAsync") { PingCount++; return Ping; }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
