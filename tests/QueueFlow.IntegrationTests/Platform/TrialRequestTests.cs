using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class TrialRequestTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CapturingSender sender = new();
    private readonly string databaseName = $"queueflow_platform_test_trial_{Guid.NewGuid():N}";
    private static readonly string[] DecisionActions = { "approve", "reject" };
    private WebApplicationFactory<Program> Host(int limit = 1000) => factory.WithWebHostBuilder(builder => {
        var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(configured);
            Assert.Equal("127.0.0.1", connection.Host);
            Assert.StartsWith("queueflow_platform_test", connection.Database);
            connection.Database = databaseName;
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", connection.ConnectionString);
        }
        builder.UseSetting("RateLimiting:TrialRequestPermitLimit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Cors:Origins:1", "https://queueflow.com.br");
        builder.ConfigureServices(services => {
            services.AddSingleton<IActivationEmailSender>(sender);
            services.AddSingleton<IClock>(new FixedClock());
        });
    });
    private static async Task Prepare(WebApplicationFactory<Program> host)
    {
        var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(configured)) Assert.Skip("Set QUEUEFLOW_PLATFORM_TEST_DATABASE to a disposable local PostgreSQL database.");
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(configured);
        Assert.Equal("127.0.0.1", connection.Host); Assert.StartsWith("queueflow_platform_test", connection.Database);
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync(Ct);
    }
    private static HttpClient Admin(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "platform@example.test"));
        return client;
    }
    private static CreateTrialRequestCommand Command() => new(" Test Owner ", $"trial-{Guid.NewGuid():N}@example.test", " Test Company ", "+55 (11) 99999-1234", true);
    private static async Task<TrialRequestDto> Submit(HttpClient anonymous, HttpClient admin, CreateTrialRequestCommand command)
    {
        using var response = await anonymous.PostAsJsonAsync("/api/v1/public/trial-requests", command, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var list = await admin.GetFromJsonAsync<TrialRequestDto[]>("/api/v1/platform/trial-requests", Json, Ct);
        return Assert.Single(list!, x => string.Equals(x.Email, command.Email!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConcurrentSubmissionAndApprovalReuseActivationAndStartExactlyOneFourteenDayTrial()
    {
        using var host = Host(); await Prepare(host);
        using var anonymous = host.CreateClient(); using var admin = Admin(host);
        var command = Command() with { Email = $"  TRIAL-{Guid.NewGuid():N}@EXAMPLE.TEST  " };
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => anonymous.PostAsJsonAsync("/api/v1/public/trial-requests", command, Ct)));
        var bodies = new List<string>();
        foreach (var response in responses) { using (response) { Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); bodies.Add(await response.Content.ReadAsStringAsync(Ct)); } }
        Assert.Single(bodies.Distinct()); Assert.DoesNotContain("invitationId", bodies[0], StringComparison.OrdinalIgnoreCase);
        var request = await Submit(anonymous, admin, command);
        Assert.Equal(TrialRequestStatus.Pending, request.Status); Assert.Equal("Test Owner", request.Name); Assert.Equal("+5511999991234", request.Phone);
        Assert.Null(request.DecidedAt); Assert.Null(request.InvitationId);
        var decisions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/approve", null, Ct)));
        Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, decisions.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in decisions) response.Dispose();
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(command.Email!.Trim().ToLowerInvariant(), sent.Email);
        var approved = (await admin.GetFromJsonAsync<TrialRequestDto>($"/api/v1/platform/trial-requests/{request.Id}", Json, Ct))!;
        Assert.Equal(TrialRequestStatus.Approved, approved.Status); Assert.NotNull(approved.DecidedAt); Assert.NotNull(approved.DecidedByPlatformUserId);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.TrialRequests.CountAsync(x => x.Email == sent.Email, Ct));
            var invitation = await db.OrganizationInvitations.SingleAsync(x => x.Id == approved.InvitationId, Ct);
            Assert.Equal("Test Owner", invitation.ResponsibleName); Assert.Equal("Test Company", invitation.OrganizationName); Assert.Equal("Trial", invitation.Plan);
            Assert.Null(invitation.ActivatedOrganizationId); Assert.Null(invitation.UsedAt);
            Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == sent.Email, Ct));
            Assert.Equal(1, await db.PlatformAuditLogs.CountAsync(x => x.ResourceId == request.Id && x.Action == "PlatformTrialRequestApproved", Ct));
        }
        var token = new Uri(sent.Url).Fragment[1..];
        using var codeResponse = await anonymous.PostAsJsonAsync("/api/v1/public/activation/request-code", new { invitationToken = token }, Ct);
        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        var code = (await codeResponse.Content.ReadFromJsonAsync<VerificationCodeRequestDto>(Json, Ct))!.DevelopmentCode;
        using var verify = await anonymous.PostAsJsonAsync("/api/v1/public/activation/verify", new { invitationToken = token, code }, Ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var authorization = (await verify.Content.ReadFromJsonAsync<ActivationAuthorizationDto>(Json, Ct))!.ActivationAuthorization;
        var activation = new { invitationToken = token, activation = new { organizationName = "Test Company", slug = $"trial-{Guid.NewGuid():N}", responsibleName = "Test Owner", password = "Test-password-123!", timeZone = "UTC", activationAuthorization = authorization } };
        using var complete = await anonymous.PostAsJsonAsync("/api/v1/public/activation/complete", activation, Ct);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var completed = (await complete.Content.ReadFromJsonAsync<ActivationCompletedDto>(Json, Ct))!;
        using var replay = await anonymous.PostAsJsonAsync("/api/v1/public/activation/complete", activation, Ct);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var owner = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == completed.OrganizationId, Ct);
            Assert.Equal(UserRole.Owner, owner.Role);
            var trial = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == completed.OrganizationId, Ct);
            Assert.Equal("Trial", trial.Plan); Assert.Equal(TimeSpan.FromDays(14), trial.TrialEndsAt - trial.CreatedAt);
        }
        using var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = sent.Email, password = "Test-password-123!" }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task DeliveryFailureRollsBackAndRetryCreatesOnlyOneLinkedInvitation()
    {
        using var host = Host(); await Prepare(host); using var anonymous = host.CreateClient(); using var admin = Admin(host);
        var command = Command(); var request = await Submit(anonymous, admin, command);
        sender.Fail = true;
        using var failed = await admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.TrialRequests.SingleAsync(x => x.Id == request.Id, Ct);
            Assert.Equal(TrialRequestStatus.Pending, stored.Status); Assert.Null(stored.InvitationId);
            Assert.False(await db.OrganizationInvitations.AnyAsync(x => x.Email == request.Email, Ct));
            Assert.False(await db.PlatformAuditLogs.AnyAsync(x => x.ResourceId == request.Id, Ct));
        }
        sender.Fail = false;
        using var retry = await admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task RejectionIsAuditedAndDuplicateSubmissionDoesNotOverwriteOrReopen()
    {
        using var host = Host(); await Prepare(host); using var anonymous = host.CreateClient(); using var admin = Admin(host);
        var command = Command(); var request = await Submit(anonymous, admin, command);
        using var rejected = await admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/reject", null, Ct);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var duplicate = await Submit(anonymous, admin, command with { Name = "Another person", CompanyName = "Overwrite" });
        Assert.Equal(request.Id, duplicate.Id); Assert.Equal(request.Name, duplicate.Name); Assert.Equal(TrialRequestStatus.Rejected, duplicate.Status);
        Assert.NotNull(duplicate.DecidedAt); Assert.Null(duplicate.InvitationId);
        using var approve = await admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode); Assert.Empty(sender.Sent);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlatformAuditLogs.CountAsync(x => x.ResourceId == request.Id && x.Action == "PlatformTrialRequestRejected", Ct));
    }

    [Fact]
    public async Task ApproveAndRejectRaceHasExactlyOneDecision()
    {
        using var host = Host(); await Prepare(host); using var anonymous = host.CreateClient(); using var admin = Admin(host);
        var request = await Submit(anonymous, admin, Command());
        var responses = await Task.WhenAll(DecisionActions.Select(action => admin.PostAsync($"/api/v1/platform/trial-requests/{request.Id}/{action}", null, Ct)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlatformAuditLogs.CountAsync(x => x.ResourceId == request.Id, Ct));
    }

    [Theory]
    [InlineData("name")][InlineData("company")][InlineData("email")][InlineData("phone")][InlineData("consent")][InlineData("length")][InlineData("control")]
    public async Task InvalidPublicInputIsRejected(string field)
    {
        using var host = Host(); using var client = host.CreateClient();
        var command = Command();
        command = field switch { "name" => command with { Name = " " }, "company" => command with { CompanyName = null }, "email" => command with { Email = "Name <user@example.test>" }, "phone" => command with { Phone = "++1234567890" }, "consent" => command with { AcceptedTerms = false }, "length" => command with { Name = new string('x', 201) }, _ => command with { Name = "Test\nName" } };
        using var response = await client.PostAsJsonAsync("/api/v1/public/trial-requests", command, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)][InlineData(UserRole.Owner)][InlineData(UserRole.Admin)][InlineData(UserRole.Manager)][InlineData(UserRole.Attendant)][InlineData(UserRole.Viewer)]
    public async Task OnlyPlatformIdentityCanReadOrDecide(UserRole? role)
    {
        using var host = Host(); using var client = host.CreateClient();
        if (role.HasValue) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role.Value, "tenant@example.test"));
        var expected = role.HasValue ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        var path = $"/api/v1/platform/trial-requests/{Guid.NewGuid()}";
        using var list = await client.GetAsync("/api/v1/platform/trial-requests", Ct); Assert.Equal(expected, list.StatusCode);
        using var detail = await client.GetAsync(path, Ct); Assert.Equal(expected, detail.StatusCode);
        foreach (var action in new[] { "approve", "reject" }) { using var response = await client.PostAsync($"{path}/{action}", null, Ct); Assert.Equal(expected, response.StatusCode); }
        using var publicList = await client.GetAsync("/api/v1/public/trial-requests", Ct); Assert.Equal(HttpStatusCode.MethodNotAllowed, publicList.StatusCode);
    }

    [Fact]
    public async Task PublicEndpointIsRateLimitedAndSupportsOnlyConfiguredCorsOrigins()
    {
        using var host = Host(1); await Prepare(host); using var client = host.CreateClient();
        var command = Command();
        using var first = await client.PostAsJsonAsync("/api/v1/public/trial-requests", command, Ct); Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var second = await client.PostAsJsonAsync("/api/v1/public/trial-requests", command, Ct); Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"));
        foreach (var origin in new[] { "https://queueflow.com.br", "https://evil.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/trial-requests");
            request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "POST"); request.Headers.Add("Access-Control-Request-Headers", "content-type");
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(origin == "https://queueflow.com.br", response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class CapturingSender : IActivationEmailSender
    {
        public bool IsConfigured => true;
        public bool SupportsInvitationDelivery => true;
        public bool Fail { get; set; }
        public ConcurrentQueue<(string Email, string Url)> Sent { get; } = new();
        public Task SendVerificationCodeAsync(string email, string code, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendInvitationAsync(string email, string activationUrl, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Simulated delivery failure.");
            Sent.Enqueue((email, activationUrl)); return Task.CompletedTask;
        }
    }
}
