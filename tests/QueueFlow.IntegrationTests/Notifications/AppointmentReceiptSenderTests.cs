using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Notifications;

namespace QueueFlow.IntegrationTests.Notifications;

public sealed class AppointmentReceiptSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
    private static OutboxMessage Message()
    {
        var receipt = new AppointmentReceipt(Guid.NewGuid(), "cliente@example.test", "João", "Empresa real", "Unidade Centro", "Consulta",
            new DateTimeOffset(2026, 10, 2, 15, 30, 0, TimeSpan.Zero), "America/Sao_Paulo", new string('a', 32));
        return new(receipt.AppointmentId, Guid.NewGuid(), AppointmentReceipt.OutboxType, JsonSerializer.Serialize(receipt), Now);
    }

    internal static AppointmentReceiptSender Sender(Handler handler, string url = "https://customer.example.test", string from = "QueueFlow <email@example.test>") =>
        new(new ResendEmailTransport(Options.Create(new ResendOptions { ApiKey = " re_synthetic_key\n", From = from }), new Clients(handler)),
            Options.Create(new AppointmentEmailOptions { CustomerPublicUrl = url }), new Environment());

    [Fact]
    public async Task ReceiptContainsAllFieldsAndNoConfirmationAction()
    {
        using var handler = new Handler();
        var sender = Sender(handler); var message = Message();
        Assert.True(sender.Prepare(message, Now)); Assert.Empty(handler.Bodies);
        await sender.DeliverAsync(message, Now, Ct);
        using var payload = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var body = payload.RootElement;
        Assert.Equal("cliente@example.test", body.GetProperty("to")[0].GetString());
        Assert.Equal("Bearer re_synthetic_key", Assert.Single(handler.Authorization));
        var text = body.GetProperty("text").GetString()!;
        foreach (var part in new[] { "João", "Empresa real", "Unidade Centro", "Consulta", "02/10/2026 12:30", "America/Sao_Paulo",
            "https://customer.example.test/meu-agendamento/" + new string('a', 32),
            "Ao chegar à unidade, informe à atendente que possui um agendamento para realizar o check-in." }) Assert.Contains(part, text);
        Assert.DoesNotContain("confirmar", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(message.ProcessedAt);
        await sender.DeliverAsync(message, Now, Ct);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task FailedDeliveryRetriesIdenticalBodyAndKeyAcrossRestart(int status)
    {
        using var handler = new Handler { Status = (HttpStatusCode)status };
        var message = Message(); var sender = Sender(handler); sender.Prepare(message, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.DeliverAsync(message, Now, Ct));
        Assert.Null(message.ProcessedAt);
        message.MarkFailed("Temporary", Now);
        Assert.True(message.NextAttemptAt > Now);
        var restored = new OutboxMessage(message.Id, message.OrganizationId, message.Type, message.Payload, message.CreatedAt);
        handler.Status = HttpStatusCode.OK;
        var restarted = Sender(handler, "https://changed.example.test", "Changed <other@example.test>");
        Assert.False(restarted.Prepare(restored, Now.AddMinutes(1)));
        await restarted.DeliverAsync(restored, Now.AddMinutes(1), Ct);
        Assert.Equal(handler.Keys[0], handler.Keys[1]);
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
        Assert.NotNull(restored.ProcessedAt);
    }

    [Fact]
    public async Task TimeoutDoesNotMarkReceiptSentAndRetainsRetryPayload()
    {
        using var handler = new Handler { Timeout = true };
        var sender = Sender(handler); var message = Message(); sender.Prepare(message, Now);
        var payload = message.Payload;
        await Assert.ThrowsAsync<TaskCanceledException>(() => sender.DeliverAsync(message, Now, Ct));
        Assert.Null(message.ProcessedAt); Assert.Equal(payload, message.Payload);
    }

    [Fact]
    public async Task ExpiredIdempotencyWindowSuspendsWithoutSendingOrClaimingSuccess()
    {
        using var handler = new Handler();
        var sender = Sender(handler); var message = Message(); sender.Prepare(message, Now);
        await sender.DeliverAsync(message, Now.AddHours(23), Ct);
        Assert.Empty(handler.Bodies); Assert.Null(message.ProcessedAt);
        Assert.Equal(DateTimeOffset.MaxValue, message.NextAttemptAt); Assert.Contains("review", message.LastError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://customer.example.test")]
    [InlineData("https://user:secret@customer.example.test")]
    [InlineData("https://customer.example.test/path")]
    [InlineData("https://customer.example.test?next=evil")]
    public void InvalidOriginDoesNotSendOrPrepare(string url)
    {
        using var handler = new Handler(); var message = Message(); var original = message.Payload;
        Assert.Throws<InvalidOperationException>(() => Sender(handler, url).Prepare(message, Now));
        Assert.Equal(original, message.Payload); Assert.Empty(handler.Bodies);
    }

    internal sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Timeout { get; set; }
        public List<string> Bodies { get; } = [];
        public List<string> Keys { get; } = [];
        public List<string> Authorization { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            Authorization.Add(request.Headers.Authorization!.ToString());
            if (Timeout) throw new TaskCanceledException();
            return new(Status) { Content = new StringContent("{}") };
        }
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
