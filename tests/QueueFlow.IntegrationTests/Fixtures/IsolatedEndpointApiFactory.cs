using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Fixtures;

// Reuse the guarded local Q02 database, with a separate schema for each endpoint fixture.
public abstract class IsolatedEndpointApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string schema = $"endpoint_{Guid.NewGuid():N}";
    private string connection = string.Empty;
    private bool schemaCreated;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_Q02_TEST_DATABASE");
        Assert.False(string.IsNullOrWhiteSpace(configured), "Set the disposable local QUEUEFLOW_Q02_TEST_DATABASE connection.");
        var settings = new NpgsqlConnectionStringBuilder(configured);
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal("queueflow_q02_test", settings.Database);
        Assert.Equal("queueflow_q02_test", settings.Username);
        settings.SearchPath = schema;
        settings.Pooling = false;
        connection = settings.ConnectionString;
        try
        {
            await using var setup = new NpgsqlConnection(connection);
            await setup.OpenAsync(TestContext.Current.CancellationToken);
            await using (var identity = new NpgsqlCommand("SELECT current_database()", setup))
                Assert.Equal("queueflow_q02_test", await identity.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup))
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            schemaCreated = true;
            // ASCII fixture dependency only; no application migrations are executed.
            await using (var normalize = new NpgsqlCommand("CREATE FUNCTION queueflow_normalize_email(text) RETURNS text LANGUAGE sql IMMUTABLE AS 'SELECT lower(btrim($1))'", setup))
                await normalize.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, new SetupIdentity());
            await using (var tables = new NpgsqlCommand(db.Database.GenerateCreateScript(), setup))
                await tables.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await SeedAsync(db, TestContext.Current.CancellationToken);
        }
        catch { await DropSchemaAsync(); throw; }
    }

    protected virtual Task SeedAsync(ApplicationDbContext db, CancellationToken ct) => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Assert.True(schemaCreated, "Initialize the disposable schema before creating the test host.");
        // Development preserves the existing localhost CORS contract; configuration is entirely explicit.
        builder.UseEnvironment("Development");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueueFlowDatabase"] = connection,
            ["Authentication:JwtKey"] = "integration-test-key-with-at-least-thirty-two-characters",
            ["Authentication:Issuer"] = "endpoint-tests",
            ["Authentication:Audience"] = "endpoint-tests",
            ["RateLimiting:UseRedis"] = "false",
            ["RateLimiting:AuthPermitLimit"] = "10",
            ["Redis:UseBackplane"] = "false",
            ["Asaas:Environment"] = "",
            ["Asaas:CheckoutEnabled"] = "false",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["Cors:Origins:0"] = "http://localhost:3000",
            ["Cors:Origins:1"] = "http://localhost:3001",
            ["Cors:Origins:2"] = "http://localhost:3002",
            ["Cors:Origins:3"] = "http://localhost:3003",
        };
        // Program reads settings while registering services, before ConfigureAppConfiguration runs.
        foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    public override async ValueTask DisposeAsync()
    {
        try { await base.DisposeAsync(); }
        finally { await DropSchemaAsync(); GC.SuppressFinalize(this); }
    }

    private async Task DropSchemaAsync()
    {
        if (!schemaCreated) return;
        await using var cleanup = new NpgsqlConnection(connection);
        await cleanup.OpenAsync(CancellationToken.None);
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup);
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
        schemaCreated = false;
    }

    private sealed class SetupIdentity : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public UserRole? Role => null;
        public bool IsAuthenticated => false;
    }
}
