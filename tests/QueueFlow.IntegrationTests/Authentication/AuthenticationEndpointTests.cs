using System.Net;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed class AuthenticationEndpointTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly HttpClient _client;

    public AuthenticationEndpointTests(QueueFlowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCurrentUserWithoutTokenReturnsUnauthorized()
    {
        using var response = await _client.GetAsync(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
