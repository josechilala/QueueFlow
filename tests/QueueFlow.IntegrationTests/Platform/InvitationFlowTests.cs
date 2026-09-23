using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class InvitationFlowTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task PrepareAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE"))) Assert.Skip("Set QUEUEFLOW_PLATFORM_TEST_DATABASE to a disposable local PostgreSQL database.");
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.StartsWith("queueflow_platform_test", connection.Database);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync(Ct);
    }

    private HttpClient PlatformClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "test-platform@example.test"));
        return client;
    }

    private static async Task<InvitationIssuedDto> IssueAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/platform/invitations", new { email = $"invitation-{Guid.NewGuid():N}@example.test", organizationName = "Test organization", responsibleName = "Test Owner", plan = "Trial" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationIssuedDto>(Json, Ct))!;
    }
    private static string Token(InvitationIssuedDto issued) => new Uri(issued.ActivationUrl).Fragment[1..];
    private static string Path(InvitationIssuedDto issued) => $"/api/v1/public/activation/{Token(issued)}";
    private static async Task<string> VerifyAsync(HttpClient client, InvitationIssuedDto issued)
    {
        using var request = await client.PostAsync($"{Path(issued)}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var code = (await request.Content.ReadFromJsonAsync<VerificationCodeRequestDto>(Json, Ct))!.DevelopmentCode;
        Assert.Matches("^[0-9]{6}$", code!);
        using var verify = await client.PostAsJsonAsync($"{Path(issued)}/verify", new { code }, Ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        return (await verify.Content.ReadFromJsonAsync<ActivationAuthorizationDto>(Json, Ct))!.ActivationAuthorization;
    }

    [Fact]
    public async Task ReissueInvalidatesOriginalAndOnlyHashIsPersisted()
    {
        await PrepareAsync();
        using var admin = PlatformClient(); using var anonymous = factory.CreateClient();
        var issued = await IssueAsync(admin);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.OrganizationInvitations.AsNoTracking().SingleAsync(x => x.Id == issued.Invitation.Id, Ct);
        Assert.Equal(factory.Services.GetRequiredService<ITokenService>().HashToken(Token(issued)), stored.TokenHash);
        Assert.DoesNotContain(Token(issued), JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        Assert.Null(typeof(OrganizationInvitation).GetProperty("Token"));
        using var reissue = await admin.PostAsync($"/api/v1/platform/invitations/{issued.Invitation.Id}/reissue", null, Ct);
        Assert.Equal(HttpStatusCode.OK, reissue.StatusCode);
        var replacement = (await reissue.Content.ReadFromJsonAsync<InvitationIssuedDto>(Json, Ct))!;
        Assert.NotEqual(Token(issued), Token(replacement));
        using var oldCode = await anonymous.PostAsync($"{Path(issued)}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, oldCode.StatusCode);
        using var hashCode = await anonymous.PostAsync($"/api/v1/public/activation/{stored.TokenHash}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, hashCode.StatusCode);
        using var list = await admin.GetAsync("/api/v1/platform/invitations", Ct);
        var listText = await list.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(Token(replacement), listText, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.TokenHash, listText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentActivationCreatesExactlyOneOrganizationOwnerAndTrial()
    {
        await PrepareAsync();
        using var admin = PlatformClient(); using var anonymous = factory.CreateClient();
        var issued = await IssueAsync(admin);
        var authorization = await VerifyAsync(anonymous, issued);
        var command = new { activationAuthorization = authorization, organizationName = "Concurrency Test", slug = $"concurrency-{Guid.NewGuid():N}", responsibleName = "Owner", password = "Test-password-123!", timeZone = "UTC" };
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => anonymous.PostAsJsonAsync($"{Path(issued)}/complete", command, Ct)));
        try
        {
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
            Assert.Equal(5, results.Count(result => result.StatusCode == HttpStatusCode.NotFound));
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var invitation = await db.OrganizationInvitations.SingleAsync(x => x.Id == issued.Invitation.Id, Ct);
            Assert.NotNull(invitation.UsedAt);
            Assert.Equal(1, await db.Organizations.CountAsync(x => x.Id == invitation.ActivatedOrganizationId, Ct));
            Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == invitation.ActivatedOrganizationId, Ct));
            Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == invitation.ActivatedOrganizationId && x.Role == QueueFlow.Domain.Enums.UserRole.Owner, Ct));
            Assert.Equal(1, await db.Subscriptions.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == invitation.ActivatedOrganizationId && x.Plan == "Trial", Ct));
            using var tenantLogin = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = issued.Invitation.Email, password = command.password }, Ct);
            Assert.Equal(HttpStatusCode.OK, tenantLogin.StatusCode);
            using var platformLogin = await anonymous.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = issued.Invitation.Email, password = command.password }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, platformLogin.StatusCode);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task FailedActivationLeavesNoOrganizationAndInvitationRemainsUnused()
    {
        await PrepareAsync(); using var admin = PlatformClient(); using var anonymous = factory.CreateClient();
        var issued = await IssueAsync(admin); var authorization = await VerifyAsync(anonymous, issued);
        var slug = $"rollback-{Guid.NewGuid():N}";
        using var response = await anonymous.PostAsJsonAsync($"{Path(issued)}/complete", new { activationAuthorization = authorization, organizationName = "Rollback", slug, responsibleName = "Owner", password = "short", timeZone = "UTC" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Organizations.AnyAsync(x => x.Slug == slug, Ct));
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == issued.Invitation.Email, Ct));
        Assert.Null((await db.OrganizationInvitations.SingleAsync(x => x.Id == issued.Invitation.Id, Ct)).UsedAt);
    }

    [Fact]
    public async Task CodesHaveCooldownAttemptLimitAndResendInvalidatesPreviousCode()
    {
        await PrepareAsync(); using var admin = PlatformClient(); using var anonymous = factory.CreateClient();
        var issued = await IssueAsync(admin);
        using var sent = await anonymous.PostAsync($"{Path(issued)}/verification-code", null, Ct);
        var oldCode = (await sent.Content.ReadFromJsonAsync<VerificationCodeRequestDto>(Json, Ct))!.DevelopmentCode;
        using var cooldown = await anonymous.PostAsync($"{Path(issued)}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, cooldown.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OrganizationInvitations\" SET \"VerificationCodeSentAt\" = {DateTimeOffset.UtcNow.AddMinutes(-2)} WHERE \"Id\" = {issued.Invitation.Id}", Ct);
        using var resent = await anonymous.PostAsync($"{Path(issued)}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var newCode = (await resent.Content.ReadFromJsonAsync<VerificationCodeRequestDto>(Json, Ct))!.DevelopmentCode;
        var invalidCode = newCode == "000000" ? "111111" : "000000";
        if (newCode != oldCode) { using var old = await anonymous.PostAsJsonAsync($"{Path(issued)}/verify", new { code = oldCode }, Ct); Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode); }
        for (var index = 0; index < 5; index++) { using var wrong = await anonymous.PostAsJsonAsync($"{Path(issued)}/verify", new { code = invalidCode }, Ct); Assert.False(wrong.IsSuccessStatusCode); }
        using var correct = await anonymous.PostAsJsonAsync($"{Path(issued)}/verify", new { code = newCode }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, correct.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-invitation")]
    [InlineData("expired-authorization")]
    [InlineData("expired-invitation")]
    [InlineData("revoked")]
    [InlineData("reused")]
    public async Task ActivationAuthorizationCannotBeStolenExpiredCrossedOrReused(string attack)
    {
        await PrepareAsync();
        using var admin = PlatformClient(); using var recipient = factory.CreateClient(); using var attacker = factory.CreateClient();
        var issued = await IssueAsync(admin);
        var authorization = await VerifyAsync(recipient, issued);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.OrganizationInvitations.AsNoTracking().SingleAsync(x => x.Id == issued.Invitation.Id, Ct);
        Assert.DoesNotContain(authorization, JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        Assert.Null(stored.VerificationCodeHash);
        if (attack == "missing") authorization = null;
        if (attack == "wrong-invitation") issued = await IssueAsync(admin);
        if (attack == "expired-authorization")
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OrganizationInvitations\" SET \"ActivationAuthorizationExpiresAt\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\" = {issued.Invitation.Id}", Ct);
        if (attack == "expired-invitation")
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OrganizationInvitations\" SET \"ExpiresAt\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\" = {issued.Invitation.Id}", Ct);
        if (attack == "revoked")
        {
            using var revoked = await admin.PostAsync($"/api/v1/platform/invitations/{issued.Invitation.Id}/revoke", null, Ct);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }
        var command = new { activationAuthorization = authorization, organizationName = "Secure activation", slug = $"secure-{Guid.NewGuid():N}", responsibleName = "Owner", password = "Secure-password-123!", timeZone = "UTC" };
        if (attack == "reused")
        {
            using var legitimate = await recipient.PostAsJsonAsync($"{Path(issued)}/complete", command, Ct);
            Assert.Equal(HttpStatusCode.OK, legitimate.StatusCode);
        }
        using var rejected = await attacker.PostAsJsonAsync($"{Path(issued)}/complete", command, Ct);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    }
}
