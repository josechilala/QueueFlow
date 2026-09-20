using System.Net;
using System.Net.Http.Json;
using QueueFlow.Application.Features.Auth;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Tenancy;

public sealed class TenantIsolationEndpointTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly QueueFlowApiFactory _factory;
    public TenantIsolationEndpointTests(QueueFlowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CompanyAUserCannotAccessCompanyBBranchById()
    {
        await using var sourceScope = _factory.Services.CreateAsyncScope();
        var sourceDb = sourceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = new NpgsqlConnectionStringBuilder(sourceDb.Database.GetConnectionString());
        var local = string.Equals(connection.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(connection.Host, out var address) && IPAddress.IsLoopback(address));
        if (!local) Assert.Skip("A local PostgreSQL server is required for the tenant isolation test.");
        if (!await sourceDb.Database.CanConnectAsync(TestContext.Current.CancellationToken)) Assert.Skip("A configured local QueueFlow PostgreSQL database is required for the tenant isolation test.");

        // Isolate all tables, functions and migration history from the public schema.
        // A local schema does not require the PostgreSQL CREATEDB privilege.
        var schema = $"queueflow_tenancy_test_{Guid.NewGuid():N}";
        await using var setupConnection = new NpgsqlConnection(connection.ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setupConnection))
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        connection.SearchPath = schema;
        try
        {
            await using var isolated = _factory.WithWebHostBuilder(builder =>
                builder.UseSetting("ConnectionStrings:QueueFlowDatabase", connection.ConnectionString));
            await using var scope = isolated.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var now = DateTimeOffset.UtcNow;
            var companyA = new Organization(Guid.NewGuid(), "Company A", $"company-a-{Guid.NewGuid():N}", "UTC", now);
            var companyB = new Organization(Guid.NewGuid(), "Company B", $"company-b-{Guid.NewGuid():N}", "UTC", now);
            var userA = new AppUser(Guid.NewGuid(), companyA.Id, "Owner A", $"owner-a-{Guid.NewGuid():N}@test.local", "unused", UserRole.Owner, now);
            var branchA = new Branch(Guid.NewGuid(), companyA.Id, "Branch A", "UTC", now);
            var branchB = new Branch(Guid.NewGuid(), companyB.Id, "Branch B", "UTC", now);
            db.AddRange(companyA, companyB, userA, branchA, branchB);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            using var client = isolated.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.CreateAccessToken(userA.Id, companyA.Id, userA.Role, userA.Email));

            var identity = await client.GetFromJsonAsync<AuthenticatedUser>("/api/v1/auth/me", TestContext.Current.CancellationToken);
            Assert.NotNull(identity);
            Assert.Equal(companyA.Id, identity.OrganizationId);
            Assert.Equal(companyA.Name, identity.OrganizationName);
            Assert.Equal(userA.Role, identity.Role);

            using var ownResponse = await client.GetAsync($"/api/v1/branches/{branchA.Id}", TestContext.Current.CancellationToken);
            using var foreignResponse = await client.GetAsync($"/api/v1/branches/{branchB.Id}", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setupConnection);
            await drop.ExecuteNonQueryAsync(cleanup.Token);
        }
    }
}
