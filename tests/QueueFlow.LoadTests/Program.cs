using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var apiUrl = Environment.GetEnvironmentVariable("QUEUEFLOW_API_URL") ?? "http://localhost:18080";
using var test = new LoadTest(new Uri(apiUrl));
await test.RunAsync();

internal sealed class LoadTest(Uri apiBase) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new(new SocketsHttpHandler { MaxConnectionsPerServer = 1_000 }) { BaseAddress = apiBase, Timeout = TimeSpan.FromMinutes(5) };

    public async Task RunAsync()
    {
        var total = Stopwatch.StartNew();
        var stamp = Guid.NewGuid().ToString("N");
        Console.WriteLine($"QueueFlow load test against {apiBase}");
        var tenantA = await CreateTenantAsync($"load-a-{stamp}", $"load-a-{stamp}@test.local", $"Load!A-{stamp}");
        var tenantB = await CreateTenantAsync($"load-b-{stamp}", $"load-b-{stamp}@test.local", $"Load!B-{stamp}");
        var context = await CreateQueueAsync(tenantA, stamp);
        var attendants = await CreateAttendantsAsync(tenantA, context.BranchId, stamp, 10);
        var issued = new ConcurrentBag<IssuedTicket>();
        await IssueBatchAsync(context.PublicId, 100, issued, "100 clientes");
        await IssueBatchAsync(context.PublicId, 500, issued, "500 clientes");
        AssertUnique(issued);
        await CallConcurrentlyAsync(context.QueueId, attendants, issued);
        await VerifySignalRReconnectionsAsync(context.PublicId, 20, 3);
        await VerifyTenantIsolationAsync(tenantB, context.QueueId);
        Console.WriteLine($"PASS: {issued.Count} tickets; IDs, senhas e tokens únicos; 10 chamadas simultâneas; 60 reconexões SignalR; isolamento entre tenants confirmado. Total: {total.Elapsed}.");
    }

    private async Task<Tenant> CreateTenantAsync(string slug, string email, string password)
    {
        var register = await SendAsync(HttpMethod.Post, "/api/v1/auth/register", null, new { name = $"Load {slug}", slug, timeZone = "America/Sao_Paulo", adminName = "Load Owner", adminEmail = email, password }, HttpStatusCode.Created);
        var organization = await ReadAsync<OrganizationCreated>(register);
        var login = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", null, new { email, password }, HttpStatusCode.OK);
        return new Tenant(organization.OrganizationId, (await ReadAsync<TokenPair>(login)).AccessToken);
    }

    private async Task<QueueContext> CreateQueueAsync(Tenant tenant, string stamp)
    {
        var branch = await ReadAsync<BranchResponse>(await SendAsync(HttpMethod.Post, "/api/v1/branches", tenant.AccessToken, new { name = "Load Branch", address = "Staging", timeZone = "America/Sao_Paulo" }, HttpStatusCode.Created));
        var service = await ReadAsync<IdResponse>(await SendAsync(HttpMethod.Post, "/api/v1/services", tenant.AccessToken, new { branchId = branch.Id, name = "Load Service", description = "Etapa 29", prefix = "L", averageDurationMinutes = 5 }, HttpStatusCode.Created));
        var queue = await ReadAsync<QueueResponse>(await SendAsync(HttpMethod.Post, "/api/v1/queues", tenant.AccessToken, new { branchId = branch.Id, serviceId = service.Id, name = $"Load Queue {stamp}", capacity = 1_000 }, HttpStatusCode.Created));
        await SendAsync(HttpMethod.Post, $"/api/v1/queues/{queue.Id}/open", tenant.AccessToken, null, HttpStatusCode.OK);
        return new QueueContext(branch.Id, queue.Id, queue.PublicId);
    }

    private async Task<IReadOnlyList<Attendant>> CreateAttendantsAsync(Tenant tenant, Guid branchId, string stamp, int count)
    {
        var result = new List<Attendant>(count);
        for (var i = 0; i < count; i++)
        {
            var counter = await ReadAsync<IdResponse>(await SendAsync(HttpMethod.Post, "/api/v1/counters", tenant.AccessToken, new { branchId, name = $"Guichê Load {i + 1:00}" }, HttpStatusCode.Created));
            var email = $"attendant-{i}-{stamp}@test.local";
            var password = $"Attendant!{i}-{stamp}";
            await SendAsync(HttpMethod.Post, "/api/v1/users", tenant.AccessToken, new { name = $"Attendant {i}", email, password, role = "Attendant", branchIds = new[] { branchId } }, HttpStatusCode.Created);
            var login = await ReadAsync<TokenPair>(await SendAsync(HttpMethod.Post, "/api/v1/auth/login", null, new { email, password }, HttpStatusCode.OK));
            result.Add(new Attendant(counter.Id, login.AccessToken));
        }
        return result;
    }

    private async Task IssueBatchAsync(string publicId, int count, ConcurrentBag<IssuedTicket> destination, string label)
    {
        var timer = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, count).Select(async _ => destination.Add(await ReadAsync<IssuedTicket>(await SendAsync(HttpMethod.Post, $"/api/v1/public/queues/{publicId}/tickets", null, new { priority = "Normal" }, HttpStatusCode.OK)))));
        Console.WriteLine($"PASS: {label} emitindo simultaneamente em {timer.Elapsed}.");
    }

    private static void AssertUnique(ConcurrentBag<IssuedTicket> issued)
    {
        Require(issued.Count == 600, $"Esperados 600 tickets, recebidos {issued.Count}.");
        Require(issued.Select(x => x.Id).Distinct().Count() == issued.Count, "Ticket ID duplicado.");
        Require(issued.Select(x => x.TicketNumber).Distinct(StringComparer.Ordinal).Count() == issued.Count, "Senha/número de ticket duplicado.");
        Require(issued.Select(x => x.CustomerPublicToken).Distinct(StringComparer.Ordinal).Count() == issued.Count, "Token público duplicado.");
    }

    private async Task CallConcurrentlyAsync(Guid queueId, IReadOnlyList<Attendant> attendants, ConcurrentBag<IssuedTicket> issued)
    {
        var calls = await Task.WhenAll(attendants.Select(async attendant => await ReadAsync<TicketOperationResponse>(await SendAsync(HttpMethod.Post, $"/api/v1/queues/{queueId}/call-next", attendant.AccessToken, new { counterId = attendant.CounterId }, HttpStatusCode.OK))));
        Require(calls.Select(x => x.Id).Distinct().Count() == attendants.Count, "Atendentes chamaram o mesmo ticket.");
        Require(calls.All(x => issued.Any(ticket => ticket.Id == x.Id)), "Uma chamada retornou ticket não emitido pelo teste.");
        Console.WriteLine($"PASS: {attendants.Count} atendentes chamaram tickets distintos simultaneamente.");
    }

    private async Task VerifyTenantIsolationAsync(Tenant otherTenant, Guid foreignQueueId)
    {
        await SendAsync(HttpMethod.Get, $"/api/v1/queues/{foreignQueueId}", otherTenant.AccessToken, null, HttpStatusCode.NotFound);
        var queues = await ReadAsync<QueueResponse[]>(await SendAsync(HttpMethod.Get, "/api/v1/queues", otherTenant.AccessToken, null, HttpStatusCode.OK));
        Require(queues.All(x => x.Id != foreignQueueId), "Tenant B recebeu fila pertencente ao tenant A.");
        Console.WriteLine("PASS: leitura direta e listagem não vazam dados entre tenants.");
    }

    private async Task VerifySignalRReconnectionsAsync(string publicQueueId, int clients, int reconnectsPerClient)
    {
        await Task.WhenAll(Enumerable.Range(0, clients).Select(async _ => { for (var i = 0; i < reconnectsPerClient; i++) await ConnectSignalRAsync(publicQueueId); }));
        Console.WriteLine($"PASS: {clients * reconnectsPerClient} conexões/reconexões SignalR concluídas.");
    }

    private async Task ConnectSignalRAsync(string publicQueueId)
    {
        var negotiation = await ReadAsync<Negotiation>(await SendAsync(HttpMethod.Post, "/hubs/queue/negotiate?negotiateVersion=1", null, null, HttpStatusCode.OK));
        Require(!string.IsNullOrWhiteSpace(negotiation.ConnectionToken), "SignalR não retornou connectionToken.");
        var url = new UriBuilder(apiBase) { Scheme = apiBase.Scheme == "https" ? "wss" : "ws", Path = "/hubs/queue", Query = $"id={Uri.EscapeDataString(negotiation.ConnectionToken)}" }.Uri;
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await socket.ConnectAsync(url, timeout.Token);
        await SendWebSocketAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e", timeout.Token);
        Require((await ReceiveWebSocketAsync(socket, timeout.Token)).StartsWith("{}", StringComparison.Ordinal), "Handshake SignalR inválido.");
        await SendWebSocketAsync(socket, $"{{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinQueueGroup\",\"arguments\":[\"{publicQueueId}\"]}}\u001e", timeout.Token);
        Require((await ReceiveWebSocketAsync(socket, timeout.Token)).Contains("\"type\":3", StringComparison.Ordinal), "JoinQueueGroup não foi confirmado.");
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect-test", timeout.Token);
    }

    private static Task SendWebSocketAsync(ClientWebSocket socket, string payload, CancellationToken ct) => socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);
    private static async Task<string> ReceiveWebSocketAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, ct);
        Require(result.MessageType == WebSocketMessageType.Text, "SignalR retornou frame não textual.");
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        var response = await _http.SendAsync(request);
        if (response.StatusCode == expected) return response;
        var detail = await response.Content.ReadAsStringAsync();
        response.Dispose();
        throw new InvalidOperationException($"{method} {path}: esperado {(int)expected}, recebido {(int)response.StatusCode}. {detail}");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response) return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new InvalidOperationException($"Resposta vazia para {typeof(T).Name}.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public void Dispose() => _http.Dispose();

    private sealed record Tenant(Guid OrganizationId, string AccessToken);
    private sealed record QueueContext(Guid BranchId, Guid QueueId, string PublicId);
    private sealed record Attendant(Guid CounterId, string AccessToken);
    private sealed record OrganizationCreated(Guid OrganizationId, Guid UserId);
    private sealed record TokenPair(string AccessToken, string RefreshToken);
    private sealed record BranchResponse(Guid Id, string PublicId);
    private sealed record IdResponse(Guid Id);
    private sealed record QueueResponse(Guid Id, string PublicId);
    private sealed record IssuedTicket(Guid Id, string TicketNumber, string CustomerPublicToken, string Status);
    private sealed record TicketOperationResponse(Guid Id, string TicketNumber, string Status);
    private sealed record Negotiation(string ConnectionToken);
}
