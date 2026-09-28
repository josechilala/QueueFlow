using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Authentication;
using StackExchange.Redis;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed class AuthenticationThrottleStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static Task<Result<TokenPair>> Valid(CancellationToken _) => Task.FromResult(Result.Success(new TokenPair("access", "refresh")));
    private static Task<Result<TokenPair>> Invalid(CancellationToken _) => Task.FromResult(Result.Failure<TokenPair>(new("auth.invalid_credentials", "Invalid credentials.")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyCredentialFailuresCountAndSuccessResetsAcrossInstances(bool distributed)
    {
        using var state = await Stores.CreateAsync(distributed);
        var first = new LoginProtection(state.First, new());
        var second = new LoginProtection(state.Second, new());
        var email = $"{Guid.NewGuid():N}@example.test";
        for (var i = 0; i < 20; i++)
            Assert.True((await first.ExecuteAsync(IdentityType.Tenant, email, Valid, Ct)).Result.IsSuccess);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            for (var i = 0; i < 9; i++)
                Assert.Equal("auth.invalid_credentials", (await first.ExecuteAsync(IdentityType.Tenant, email, Invalid, Ct)).Result.Error.Code);
            Assert.True((await second.ExecuteAsync(IdentityType.Tenant, " " + email.ToUpperInvariant() + " ", Valid, Ct)).Result.IsSuccess);
        }
        for (var i = 0; i < 12; i++)
            await first.ExecuteAsync(IdentityType.Tenant, email, _ => Task.FromResult(Result.Failure<TokenPair>(new("dependency.unavailable", "Unavailable"))), Ct);
        Assert.True((await second.ExecuteAsync(IdentityType.Tenant, email, Valid, Ct)).Result.IsSuccess);
        var attempts = Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? first : second).ExecuteAsync(IdentityType.Tenant, email, Invalid, Ct));
        var results = await Task.WhenAll(attempts);
        Assert.Equal(10, results.Count(x => x.Result.Error.Code == "auth.invalid_credentials"));
        Assert.Equal(10, results.Count(x => x.Result.Error.Code == "auth.login_limited"));
        var blocked = await second.ExecuteAsync(IdentityType.Tenant, email, Valid, Ct);
        Assert.Equal("auth.login_limited", blocked.Result.Error.Code);
        Assert.InRange(blocked.RetryAfter!.Value.TotalSeconds, 1, 60);
        Assert.True((await second.ExecuteAsync(IdentityType.Platform, email, Valid, Ct)).Result.IsSuccess);
        Assert.True((await second.ExecuteAsync(IdentityType.Tenant, "other-" + email, Valid, Ct)).Result.IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshBudgetIsDistributedAndIndependentFromLogin(bool distributed)
    {
        using var state = await Stores.CreateAsync(distributed);
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            (i % 2 == 0 ? state.First : state.Second).AcquireRefreshAsync(key, 30, Window, Ct)));
        Assert.Equal(30, results.Count(x => x == TimeSpan.Zero));
        Assert.Equal(10, results.Count(x => x > TimeSpan.Zero));
        Assert.Equal(LoginAdmission.Allowed, (await state.Second.BeginLoginAsync(key, "owner", 10, Window, Window, Ct)).Admission);
        Assert.True(await state.Second.FinishLoginAsync(key, "owner", LoginAttemptOutcome.Succeeded, Window, Ct));
        Assert.True(await state.First.AcquireRefreshAsync(key, 30, Window, Ct) > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredFailuresRecoverAndStaleOwnersCannotResetState(bool distributed)
    {
        using var state = await Stores.CreateAsync(distributed);
        var key = Guid.NewGuid().ToString("N");
        var shortWindow = TimeSpan.FromMilliseconds(100);
        await state.First.BeginLoginAsync(key, "old", 1, Window, shortWindow, Ct);
        await Task.Delay(200, Ct);
        Assert.Equal(LoginAdmission.Allowed, (await state.Second.BeginLoginAsync(key, "new", 1, Window, Window, Ct)).Admission);
        Assert.False(await state.First.FinishLoginAsync(key, "old", LoginAttemptOutcome.Succeeded, Window, Ct));
        Assert.True(await state.Second.FinishLoginAsync(key, "new", LoginAttemptOutcome.Failed, shortWindow, Ct));
        Assert.Equal(LoginAdmission.Blocked, (await state.First.BeginLoginAsync(key, "blocked", 1, Window, Window, Ct)).Admission);
        await Task.Delay(200, Ct);
        Assert.Equal(LoginAdmission.Allowed, (await state.First.BeginLoginAsync(key, "recovered", 1, Window, Window, Ct)).Admission);
        await state.First.FinishLoginAsync(key, "recovered", LoginAttemptOutcome.Succeeded, Window, Ct);
    }

    [Fact]
    public async Task RedisUnavailableFailsClosedWithoutClaimingInvalidCredentials()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100,connectRetry=0");
        var protection = new LoginProtection(new RedisAuthenticationThrottleStore(redis), new());
        var called = false;
        var result = await protection.ExecuteAsync(IdentityType.Tenant, "unknown@example.test", token => { called = true; return Valid(token); }, Ct);
        Assert.False(called);
        Assert.Equal("auth.temporarily_unavailable", result.Result.Error.Code);
        Assert.DoesNotContain("Redis", result.Result.Error.Description);
    }

    [Fact]
    public void PasswordVerificationRejectsMissingAndIncorrectHashes()
    {
        var passwords = new PasswordService();
        var hash = passwords.Hash("correct-password");
        Assert.True(passwords.Verify(hash, "correct-password"));
        Assert.False(passwords.Verify(hash, "incorrect-password"));
        Assert.False(passwords.Verify(null, "correct-password"));
    }

    [Fact]
    public async Task CanceledAuthenticationDoesNotCountAndReleasesItsLease()
    {
        var protection = new LoginProtection(new InMemoryAuthenticationThrottleStore(TimeProvider.System), new());
        const string email = "canceled@example.test";
        for (var i = 0; i < 12; i++)
            await Assert.ThrowsAsync<OperationCanceledException>(() => protection.ExecuteAsync(IdentityType.Tenant, email,
                _ => throw new OperationCanceledException(), Ct));
        Assert.True((await protection.ExecuteAsync(IdentityType.Tenant, email, Valid, Ct)).Result.IsSuccess);
    }

    private sealed class Stores(IAuthenticationThrottleStore first, IAuthenticationThrottleStore second, ConnectionMultiplexer? connection1 = null, ConnectionMultiplexer? connection2 = null) : IDisposable
    {
        public IAuthenticationThrottleStore First { get; } = first;
        public IAuthenticationThrottleStore Second { get; } = second;
        public static async Task<Stores> CreateAsync(bool distributed)
        {
            if (!distributed)
            {
                var memory = new InMemoryAuthenticationThrottleStore(TimeProvider.System);
                return new(memory, memory);
            }
            var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_AUTH_TEST_REDIS");
            if (string.IsNullOrWhiteSpace(configured)) Assert.Skip("Set QUEUEFLOW_AUTH_TEST_REDIS to a disposable local Redis instance.");
            Assert.StartsWith("127.0.0.1:", configured);
            var first = await ConnectionMultiplexer.ConnectAsync(configured);
            var second = await ConnectionMultiplexer.ConnectAsync(configured);
            return new(new RedisAuthenticationThrottleStore(first), new RedisAuthenticationThrottleStore(second), first, second);
        }
        public void Dispose() { connection1?.Dispose(); connection2?.Dispose(); }
    }
}
