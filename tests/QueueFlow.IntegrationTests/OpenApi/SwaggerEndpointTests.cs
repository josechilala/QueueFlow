using System.Net;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.OpenApi;

public sealed class SwaggerEndpointTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly HttpClient _client;

    public SwaggerEndpointTests(QueueFlowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSwaggerUiReturnsOk()
    {
        using var response = await _client.GetAsync(
            "/swagger/index.html",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
