using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace QueueFlow.IntegrationTests.Health;

public sealed class LiveHealthEndpointTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly HttpClient _client;

    public LiveHealthEndpointTests(QueueFlowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetLiveHealthReturnsOk()
    {
        using var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationIdIsReturnedAndValidated()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "stage-27-test");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("stage-27-test", response.Headers.GetValues("X-Correlation-ID").Single());
    }
}

public sealed class QueueFlowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:JwtKey", "integration-test-key-with-at-least-thirty-two-characters");
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
