using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueueFlow.Infrastructure.Authentication;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Persistence;

namespace QueueFlow.IntegrationTests.Health;

public sealed class ReadinessHealthCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadinessFailureIs503WhileLivenessDoesNotOpenDatabase(bool timeout)
    {
        var interceptor = new OpeningInterceptor(timeout);
        await using var original = new BillingEndpointSecurityTests.OfflineFactory();
        await using var factory = original.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.AddScoped(_ => Context(interceptor));
        }));
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(0, interceptor.Calls);
        using var ready = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(1, interceptor.Calls);
        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.All(registrations.Where(x => x.Tags.Contains("ready")), x => Assert.Equal(TimeSpan.FromSeconds(4), x.Timeout));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("timeout")]
    [InlineData("caller-canceled")]
    public async Task PostgreSqlDistinguishesFailureTimeoutAndCallerCancellationWithoutProviderMessages(string scenario)
    {
        await using var original = new BillingEndpointSecurityTests.OfflineFactory();
        using var client = original.CreateClient();
        var registration = original.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single(x => x.Name == "postgresql");
        var logs = new HealthLogCapture();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        await using var db = Context(new OpeningInterceptor(scenario != "unavailable"));
        using var services = new ServiceCollection().AddLogging().AddSingleton<ILoggerFactory>(loggerFactory).AddSingleton(db).BuildServiceProvider();
        var check = registration.Factory(services);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (scenario == "caller-canceled") await cancellation.CancelAsync();

        if (scenario == "caller-canceled")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new(), cancellation.Token));
            Assert.Contains(logs.Entries, x => x.Message.Contains("canceled by caller", StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Entries, x => x.Level >= LogLevel.Warning);
        }
        else
        {
            var result = await check.CheckHealthAsync(new(), cancellation.Token);
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Null(result.Exception);
            Assert.Contains(logs.Entries, x => x.Message.Contains(scenario == "timeout" ? "internal timeout" : "dependency unavailable", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(logs.Entries, x => x.Message.Contains("private-provider-detail", StringComparison.Ordinal) || x.Exception is not null);
    }

    private static ApplicationDbContext Context(OpeningInterceptor interceptor) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=health_test;Username=health_test;Password=health_test")
            .AddInterceptors(interceptor).Options,
        new CurrentUser(new HttpContextAccessor()));

    private sealed class OpeningInterceptor(bool wait) : DbConnectionInterceptor
    {
        public int Calls { get; private set; }
        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("private-provider-detail");
        }
    }
}
