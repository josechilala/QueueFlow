using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Infrastructure.Realtime;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class RealtimePrivacyTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("appointment.created")]
    [InlineData("appointment.cancelled")]
    [InlineData("ticket.called")]
    [InlineData("ticket.issued")]
    public async Task PublicServiceEventsNeverExposeCapabilitiesEvenInNestedPayloads(string eventName)
    {
        await using var database = await PlatformMigrationTests.CreateDatabaseAsync();
        await database.Database.MigrateAsync(Ct);
        var capture = new CaptureHub();
        using var isolated = factory.WithWebHostBuilder(builder => {
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", database.Database.GetConnectionString());
            builder.ConfigureServices(services => services.AddSingleton<IHubContext<QueueHub>>(capture));
        });
        var now = DateTimeOffset.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Privacy", $"privacy-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Privacy", "UTC", now);
        var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Privacy", null, "PR", 30, now);
        var appointment = new Appointment(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Private name", null, "private@example.test", now.AddHours(3), now.AddHours(4), "UTC", false, now);
        database.AddRange(organization, branch, service, appointment);
        await database.SaveChangesAsync(Ct);
        await isolated.Services.GetRequiredService<IQueueRealtimeNotifier>().QueueEventAsync(service.PublicId, eventName,
            new { appointmentToken = appointment.PublicToken, customerName = "Private name", nested = new { managementToken = "secret-capability", password = "secret-password" } }, Ct);
        Assert.NotEmpty(capture.Events);
        foreach (var entry in capture.Events)
        {
            Assert.DoesNotContain(appointment.PublicToken, entry.Payload, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-", entry.Payload, StringComparison.Ordinal);
            Assert.DoesNotContain("Private name", entry.Payload, StringComparison.Ordinal);
        }
        using var anonymous = isolated.CreateClient();
        using var cancel = await anonymous.PostAsync($"/api/v1/public/appointments/{appointment.Id}/cancel", null, Ct);
        Assert.False(cancel.IsSuccessStatusCode);
        Assert.Equal(appointment.Status, (await database.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct)).Status);
    }
}

internal sealed class CaptureHub : IHubContext<QueueHub>
{
    internal ConcurrentBag<(string Group, string Method, string Payload)> Events { get; } = [];
    public IHubClients Clients => new CaptureClients(Events);
    public IGroupManager Groups => new CaptureGroups();
}
internal sealed class CaptureProxy(string group, ConcurrentBag<(string Group, string Method, string Payload)> events) : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) { events.Add((group, method, JsonSerializer.Serialize(args))); return Task.CompletedTask; }
}
internal sealed class CaptureClients(ConcurrentBag<(string Group, string Method, string Payload)> events) : IHubClients
{
    public IClientProxy All => new CaptureProxy("all", events);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
    public IClientProxy Client(string connectionId) => All;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
    public IClientProxy Group(string groupName) => new CaptureProxy(groupName, events);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
    public IClientProxy User(string userId) => All;
    public IClientProxy Users(IReadOnlyList<string> userIds) => All;
}
internal sealed class CaptureGroups : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
