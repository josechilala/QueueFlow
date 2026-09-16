using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class PlatformMigrationTests
{
    private const string Previous = "20260914061709_EnforceSingleActiveQueuePerService";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("\tUser@Example.test\r\n")]
    [InlineData("\u00a0ÜSER@EXAMPLE.TEST\u2003")]
    [InlineData("İΣẞ@EXAMPLE.TEST")]
    public async Task SqlNormalizationMatchesInvariantDotNet(string email)
    {
        await using var db = await CreateDatabaseAsync(); await db.Database.MigrateAsync(Ct);
        var actual = await db.Database.SqlQuery<string>($"SELECT queueflow_normalize_email({email}) AS \"Value\"").SingleAsync(Ct);
        Assert.Equal(email.Trim().ToLowerInvariant(), actual);
    }

    [Fact]
    public async Task NoncanonicalLegacyEmailAbortsWithoutRewritingIt()
    {
        await using var db = await CreateDatabaseAsync();
        var user = new AppUser(Guid.NewGuid(), Guid.NewGuid(), "Legacy", "legacy@example.test", "unused", UserRole.Owner, DateTimeOffset.UtcNow);
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        const string raw = "\tLegacy@example.test";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"Email\" = {raw} WHERE \"Id\" = {user.Id}", Ct);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync(Ct));
        Assert.Contains("Noncanonical", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(raw, await db.Users.IgnoreQueryFilters().Select(x => x.Email).SingleAsync(Ct));
    }

    internal static async Task<ApplicationDbContext> CreateDatabaseAsync()
    {
        var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(configured)) Assert.Skip("Set QUEUEFLOW_PLATFORM_TEST_DATABASE to a disposable local PostgreSQL database.");
        var connection = new NpgsqlConnectionStringBuilder(configured);
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.StartsWith("queueflow_platform_test", connection.Database);
        connection.Database = $"queueflow_platform_test_{Guid.NewGuid():N}";
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options, new TestIdentity(null));
        await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
        return db;
    }

    [Fact]
    public async Task DuplicateEmailPreflightFailsWithoutChangingUsersOrCreatingPlatformTables()
    {
        await using var db = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var first = new AppUser(Guid.NewGuid(), Guid.NewGuid(), "First", "duplicate@example.test", "test-hash", UserRole.Owner, now);
        var second = new AppUser(Guid.NewGuid(), Guid.NewGuid(), "Second", "duplicate@example.test", "test-hash", UserRole.Owner, now);
        db.Users.AddRange(first, second); await db.SaveChangesAsync(Ct);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync(Ct));
        Assert.Contains("duplicate normalized emails", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(2, await db.Users.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Contains("20260915060310_AddPlatformAdministrationAndInvitations", await db.Database.GetPendingMigrationsAsync(Ct));
        var tables = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = 'PlatformUsers'").SingleAsync(Ct);
        Assert.Equal(0, tables);
    }

    [Fact]
    public async Task MigrationUpDownUpPreservesTenantDataAndEnforcesGlobalEmail()
    {
        await using var db = await CreateDatabaseAsync();
        var email = $"unique-{Guid.NewGuid():N}@example.test";
        db.Users.Add(new AppUser(Guid.NewGuid(), Guid.NewGuid(), "First", email, "test-hash", UserRole.Owner, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        Assert.False((await db.Database.GetPendingMigrationsAsync(Ct)).Any());
        db.Users.Add(new AppUser(Guid.NewGuid(), Guid.NewGuid(), "Duplicate", email, "test-hash", UserRole.Owner, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<QueueFlow.Application.Common.ConflictException>(() => db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(Ct));
        await db.Database.MigrateAsync(Ct);
        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(Ct));
    }
}
