using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Application.Features.Users;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Auditing;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed partial class JwtRevocationTests
{
    private const string ReplacementPassword = "Replacement-password-123!";

    [Theory]
    [InlineData("login", "password", true)]
    [InlineData("login", "disabled", true)]
    [InlineData("login", "demoted", true)]
    [InlineData("refresh", "password", true)]
    [InlineData("refresh", "disabled", true)]
    [InlineData("refresh", "demoted", true)]
    [InlineData("login", "password", false)]
    [InlineData("login", "disabled", false)]
    [InlineData("login", "demoted", false)]
    [InlineData("refresh", "password", false)]
    [InlineData("refresh", "disabled", false)]
    [InlineData("refresh", "demoted", false)]
    public async Task TenantSessionAndSecurityChangeAreSerialized(string operation, string change, bool sessionFirst)
    {
        await using var test = await LocalTest.CreateAsync();
        var initial = await test.LoginAsync();
        var gate = new BeforeSaveGate();
        var waiting = new UserLockObserver();
        await using var sessionDb = test.Context(null, sessionFirst ? gate : waiting);
        await using var changeDb = test.Context(new OwnerIdentity(test.Owner), sessionFirst ? waiting : gate);
        var auth = TenantAuth(test, sessionDb);
        var management = Management(test, changeDb);
        Task<Result<TokenPair>> Session() => operation == "login"
            ? auth.LoginAsync(test.Target.Email, LocalTest.Password, Ct)
            : auth.RefreshAsync(initial.RefreshToken, Ct);

        Task<Result<TokenPair>> sessionTask;
        Task changeTask;
        try
        {
            if (sessionFirst)
            {
                // Pause after credential validation/token rotation, immediately before persistence.
                sessionTask = Session();
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
                changeTask = ChangeAsync(management, test, change);
            }
            else
            {
                // Pause after the revocation query has enumerated existing tokens, before saving.
                changeTask = ChangeAsync(management, test, change);
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
                sessionTask = Session();
            }
            await AssertBlockedAsync(test, waiting);
            gate.Release();
            await Task.WhenAll(sessionTask, changeTask).WaitAsync(TimeSpan.FromSeconds(60), Ct);
        }
        finally { gate.Release(); }

        await using var check = test.Context();
        var result = await sessionTask;
        var active = await check.RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == test.Target.Id && x.RevokedAt == null).ToListAsync(Ct);
        if (!sessionFirst && operation == "login" && change == "demoted")
        {
            // Credentials are still valid: a fresh login must reflect the reduced role.
            Assert.True(result.IsSuccess);
            Assert.Single(active);
            await AssertAccessAsync(test, result.Value, UserRole.Attendant);
        }
        else
        {
            Assert.Empty(active);
            Assert.Equal(sessionFirst, result.IsSuccess);
            if (result.IsSuccess)
            {
                await AssertRevokedAsync(test, result.Value);
                await using var replayDb = test.Context();
                Assert.True((await TenantAuth(test, replayDb).RefreshAsync(result.Value.RefreshToken, Ct)).IsFailure);
            }
        }
        await using var oldDb = test.Context();
        Assert.True((await TenantAuth(test, oldDb).RefreshAsync(initial.RefreshToken, Ct)).IsFailure);
        if (change != "disabled")
        {
            await using var freshDb = test.Context();
            var fresh = await TenantAuth(test, freshDb).LoginAsync(test.Target.Email,
                change == "password" ? ReplacementPassword : LocalTest.Password, Ct);
            Assert.True(fresh.IsSuccess);
            await AssertAccessAsync(test, fresh.Value, change == "demoted" ? UserRole.Attendant : UserRole.Admin);
        }
    }

    [Theory]
    [InlineData("login", "password", true)]
    [InlineData("login", "disabled", true)]
    [InlineData("refresh", "password", true)]
    [InlineData("refresh", "disabled", true)]
    [InlineData("login", "password", false)]
    [InlineData("login", "disabled", false)]
    [InlineData("refresh", "password", false)]
    [InlineData("refresh", "disabled", false)]
    public async Task PlatformSessionUsesUserBeforeTokenLockOrder(string operation, string change, bool sessionFirst)
    {
        await using var test = await LocalTest.CreateAsync();
        var passwords = test.Factory.Services.GetRequiredService<IPasswordService>();
        var platform = new PlatformUser(Guid.NewGuid(), "Platform", "platform@example.test", passwords.Hash(LocalTest.Password), DateTimeOffset.UtcNow);
        await using (var seed = test.Context()) { seed.Add(platform); await seed.SaveChangesAsync(Ct); }
        TokenPair initial;
        await using (var seed = test.Context()) initial = (await PlatformAuth(test, seed).LoginAsync(platform.Email, LocalTest.Password, Ct)).Value;
        var gate = new BeforeSaveGate();
        var waiting = new UserLockObserver();
        await using var sessionDb = test.Context(null, sessionFirst ? gate : waiting);
        await using var changeDb = test.Context(null, sessionFirst ? waiting : gate);
        var auth = PlatformAuth(test, sessionDb);
        Task<Result<TokenPair>> Session() => operation == "login"
            ? auth.LoginAsync(platform.Email, LocalTest.Password, Ct) : auth.RefreshAsync(initial.RefreshToken, Ct);

        // There is no platform security-management endpoint. Exercise the required atomic writer protocol.
        async Task Change()
        {
            await using var transaction = await changeDb.BeginTransactionAsync(Ct);
            var user = await changeDb.PlatformUsers.FromSqlInterpolated($"SELECT * FROM \"PlatformUsers\" WHERE \"Id\" = {platform.Id} FOR UPDATE").SingleAsync(Ct);
            if (change == "password") user.SetPasswordHash(passwords.Hash(ReplacementPassword), DateTimeOffset.UtcNow);
            else user.SetActive(false, DateTimeOffset.UtcNow);
            foreach (var token in await changeDb.PlatformRefreshTokens.Where(x => x.PlatformUserId == user.Id && x.RevokedAt == null).ToListAsync(Ct))
                token.Revoke(DateTimeOffset.UtcNow);
            await changeDb.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
        Task<Result<TokenPair>> sessionTask;
        Task changeTask;
        try
        {
            if (sessionFirst)
            {
                sessionTask = Session();
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
                changeTask = Change();
            }
            else
            {
                changeTask = Change();
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
                sessionTask = Session();
            }
            await AssertBlockedAsync(test, waiting);
            gate.Release();
            await Task.WhenAll(sessionTask, changeTask).WaitAsync(TimeSpan.FromSeconds(60), Ct);
        }
        finally { gate.Release(); }
        var result = await sessionTask;
        Assert.Equal(sessionFirst, result.IsSuccess);
        await using var check = test.Context();
        Assert.False(await check.PlatformRefreshTokens.AnyAsync(x => x.PlatformUserId == platform.Id && x.RevokedAt == null, Ct));
        if (result.IsSuccess)
        {
            await using var replayDb = test.Context();
            Assert.True((await PlatformAuth(test, replayDb).RefreshAsync(result.Value.RefreshToken, Ct)).IsFailure);
        }
        if (change == "password")
        {
            await using var freshDb = test.Context();
            var fresh = await PlatformAuth(test, freshDb).LoginAsync(platform.Email, ReplacementPassword, Ct);
            Assert.True(fresh.IsSuccess);
            await using var rotateDb = test.Context();
            Assert.True((await PlatformAuth(test, rotateDb).RefreshAsync(fresh.Value.RefreshToken, Ct)).IsSuccess);
        }
    }

    [Theory]
    [InlineData("password")]
    [InlineData("disabled")]
    [InlineData("demoted")]
    public async Task RefreshLookupBeforeRevocationCannotResurrectSession(string change)
    {
        await using var test = await LocalTest.CreateAsync();
        var initial = await test.LoginAsync();
        var gate = new BeforeUserLockGate();
        await using var refreshDb = test.Context(null, gate);
        var refreshTask = TenantAuth(test, refreshDb).RefreshAsync(initial.RefreshToken, Ct);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
            await using var changeDb = test.Context(new OwnerIdentity(test.Owner));
            await ChangeAsync(Management(test, changeDb), test, change);
        }
        finally { gate.Release(); }
        Assert.True((await refreshTask.WaitAsync(TimeSpan.FromSeconds(60), Ct)).IsFailure);
        await using var check = test.Context();
        Assert.False(await check.RefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.UserId == test.Target.Id && x.RevokedAt == null, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRefreshHasOneWinnerAndDoesNotRevokeSuccessor(bool platform)
    {
        await using var test = await LocalTest.CreateAsync();
        TokenPair initial;
        await using (var seed = test.Context())
        {
            if (platform)
            {
                var user = new PlatformUser(Guid.NewGuid(), "Platform", "platform@example.test", test.Target.PasswordHash, DateTimeOffset.UtcNow);
                seed.Add(user);
                await seed.SaveChangesAsync(Ct);
                initial = (await PlatformAuth(test, seed).LoginAsync(user.Email, LocalTest.Password, Ct)).Value;
            }
            else initial = await test.LoginAsync();
        }
        var firstGate = new BeforeUserLockGate();
        var secondGate = new BeforeUserLockGate();
        await using var firstDb = test.Context(null, firstGate);
        await using var secondDb = test.Context(null, secondGate);
        Task<Result<TokenPair>> Refresh(ApplicationDbContext db) => platform
            ? PlatformAuth(test, db).RefreshAsync(initial.RefreshToken, Ct)
            : TenantAuth(test, db).RefreshAsync(initial.RefreshToken, Ct);
        var first = Refresh(firstDb);
        var second = Refresh(secondDb);
        try
        {
            await Task.WhenAll(firstGate.Reached.Task, secondGate.Reached.Task).WaitAsync(TimeSpan.FromSeconds(60), Ct);
        }
        finally { firstGate.Release(); secondGate.Release(); }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60), Ct);
        var winner = Assert.Single(results, x => x.IsSuccess).Value;
        Assert.Equal(platform ? "platform.refresh_conflict" : "auth.refresh_conflict", Assert.Single(results, x => x.IsFailure).Error.Code);
        await using var nextDb = test.Context();
        var next = platform ? await PlatformAuth(test, nextDb).RefreshAsync(winner.RefreshToken, Ct)
            : await TenantAuth(test, nextDb).RefreshAsync(winner.RefreshToken, Ct);
        Assert.True(next.IsSuccess);
    }

    [Fact]
    public async Task CancelledLoginRollsBackSessionAndReleasesUserLock()
    {
        await using var test = await LocalTest.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var gate = new BeforeSaveGate();
        await using var loginDb = test.Context(null, gate);
        var login = TenantAuth(test, loginDb).LoginAsync(test.Target.Email, LocalTest.Password, cancellation.Token);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
        await using var check = test.Context(new OwnerIdentity(test.Owner));
        Assert.False(await check.RefreshTokens.IgnoreQueryFilters().AnyAsync(Ct));
        await ChangeAsync(Management(test, check), test, "password").WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    [Fact]
    public async Task LoginForAnotherTenantDoesNotWaitForLockedUser()
    {
        await using var test = await LocalTest.CreateAsync();
        var foreign = new Organization(Guid.NewGuid(), "Other", $"other-{Guid.NewGuid():N}", "UTC", DateTimeOffset.UtcNow);
        var user = new AppUser(Guid.NewGuid(), foreign.Id, "Other", "other@example.test", test.Target.PasswordHash, UserRole.Owner, DateTimeOffset.UtcNow);
        await using (var seed = test.Context()) { seed.AddRange(foreign, user); await seed.SaveChangesAsync(Ct); }
        var gate = new BeforeSaveGate();
        await using var firstDb = test.Context(null, gate);
        var first = TenantAuth(test, firstDb).LoginAsync(test.Target.Email, LocalTest.Password, Ct);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
            await using var otherDb = test.Context();
            var other = await TenantAuth(test, otherDb).LoginAsync(user.Email, LocalTest.Password, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.True(other.IsSuccess);
            Assert.False(first.IsCompleted);
        }
        finally { gate.Release(); }
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(60), Ct)).IsSuccess);
    }

    private static AuthService TenantAuth(LocalTest test, ApplicationDbContext db) => new(db,
        test.Factory.Services.GetRequiredService<ITokenService>(), test.Factory.Services.GetRequiredService<IPasswordService>(),
        test.Factory.Services.GetRequiredService<IClock>(), new SetupIdentity());

    private static PlatformAuthService PlatformAuth(LocalTest test, ApplicationDbContext db) => new(db,
        test.Factory.Services.GetRequiredService<ITokenService>(), test.Factory.Services.GetRequiredService<IPasswordService>(),
        test.Factory.Services.GetRequiredService<IClock>(), new SetupIdentity());

    private static UserManagementService Management(LocalTest test, ApplicationDbContext db)
    {
        var identity = new OwnerIdentity(test.Owner);
        var clock = test.Factory.Services.GetRequiredService<IClock>();
        return new(db, identity, clock, test.Factory.Services.GetRequiredService<IPasswordService>(),
            new AuditWriter(db, identity, clock, new HttpContextAccessor()));
    }

    private static async Task ChangeAsync(UserManagementService management, LocalTest test, string change)
    {
        if (change == "password") Assert.True((await management.ResetPasswordAsync(test.Target.Id, ReplacementPassword, Ct)).IsSuccess);
        else if (change == "disabled") Assert.True((await management.SetStatusAsync(test.Target.Id, false, Ct)).IsSuccess);
        else Assert.True((await management.UpdateAsync(test.Target.Id, new("Target", UserRole.Attendant, [test.Branch.Id]), Ct)).IsSuccess);
    }

    private static async Task AssertAccessAsync(LocalTest test, TokenPair pair, UserRole role)
    {
        await using var scope = test.Factory.Services.CreateAsyncScope();
        await using var db = test.Context();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == test.Target.Id, Ct);
        Assert.Equal(role, user.Role);
        var validator = scope.ServiceProvider.GetRequiredService<IAccessTokenValidator>();
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken);
        var version = long.Parse(jwt.Claims.Single(x => x.Type == "access_version").Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(await validator.ValidateAsync(new(user.Id, user.OrganizationId, "tenant", role.ToString(), version), Ct));
        Assert.Contains(jwt.Claims, x => x.Value == role.ToString());
    }

    private static async Task AssertRevokedAsync(LocalTest test, TokenPair pair)
    {
        using var client = test.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);
        using var response = await client.GetAsync("/api/v1/users", Ct);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertBlockedAsync(LocalTest test, UserLockObserver observer)
    {
        var pid = await observer.Executing.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        await using var monitor = test.Context();
        await monitor.Database.OpenConnectionAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var command = monitor.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT cardinality(pg_blocking_pids(@pid)) > 0";
            command.Parameters.Add(new NpgsqlParameter("pid", pid));
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class BeforeSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult(true);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            return result;
        }
    }

    private sealed class BeforeUserLockGate : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult(true);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (IsUserLock(command))
            {
                Reached.TrySetResult(true);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }
            return result;
        }
    }

    private static bool IsUserLock(DbCommand command) => command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) &&
        (command.CommandText.Contains("\"Users\"", StringComparison.Ordinal) || command.CommandText.Contains("\"PlatformUsers\"", StringComparison.Ordinal));

    private sealed class UserLockObserver : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Executing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (IsUserLock(command))
                Executing.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class OwnerIdentity(AppUser owner) : ICurrentUser
    {
        public Guid? UserId => owner.Id;
        public Guid? OrganizationId => owner.OrganizationId;
        public UserRole? Role => owner.Role;
        public IdentityType? IdentityType => QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => true;
    }
}
