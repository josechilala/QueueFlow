using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(TestContext.Current.CancellationToken)) Assert.Skip("A configured QueueFlow PostgreSQL database is required for the tenant isolation test.");

        var now = DateTimeOffset.UtcNow;
        var companyA = new Organization(Guid.NewGuid(), "Company A", $"company-a-{Guid.NewGuid():N}", "UTC", now);
        var companyB = new Organization(Guid.NewGuid(), "Company B", $"company-b-{Guid.NewGuid():N}", "UTC", now);
        var userA = new AppUser(Guid.NewGuid(), companyA.Id, "Owner A", $"owner-a-{Guid.NewGuid():N}@test.local", "unused", UserRole.Owner, now);
        var branchA = new Branch(Guid.NewGuid(), companyA.Id, "Branch A", "UTC", now);
        var branchB = new Branch(Guid.NewGuid(), companyB.Id, "Branch B", "UTC", now);
        db.AddRange(companyA, companyB, userA, branchA, branchB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        try
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.CreateAccessToken(userA.Id, companyA.Id, userA.Role, userA.Email));

            using var ownResponse = await client.GetAsync($"/api/v1/branches/{branchA.Id}", TestContext.Current.CancellationToken);
            using var foreignResponse = await client.GetAsync($"/api/v1/branches/{branchB.Id}", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        }
        finally
        {
            await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await db.Users.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await db.Organizations.Where(x => x.Id == companyA.Id || x.Id == companyB.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
    }
}
