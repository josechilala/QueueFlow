using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class BranchTests
{
    [Fact]
    public void UpdateChangesDetailsAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var branch = Create(now);

        branch.Update("Unidade Centro", "Rua Principal, 10", "America/Sao_Paulo", now.AddMinutes(1));

        Assert.Equal("Unidade Centro", branch.Name);
        Assert.Equal("Rua Principal, 10", branch.Address);
        Assert.Equal("America/Sao_Paulo", branch.TimeZone);
        Assert.Equal(now.AddMinutes(1), branch.UpdatedAt);
    }

    [Fact]
    public void SetActiveDeactivatesWithoutDeletingBranch()
    {
        var branch = Create(DateTimeOffset.UtcNow);

        branch.SetActive(false, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.False(branch.IsActive);
    }

    [Fact]
    public void UpdateRejectsEmptyName()
    {
        var branch = Create(DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => branch.Update(" ", null, "America/Sao_Paulo", DateTimeOffset.UtcNow));
    }

    private static Branch Create(DateTimeOffset now) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Unidade Inicial",
        null,
        "America/Sao_Paulo",
        now);
}
