using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class QueueCounterTests
{
    [Fact]
    public void CanRenameAndDeactivateCounter()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var counter = new QueueCounter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Guichê 01 ", createdAt);

        counter.Update("Sala 3", createdAt.AddMinutes(1));
        counter.SetActive(false, createdAt.AddMinutes(2));

        Assert.Equal("Sala 3", counter.Name);
        Assert.False(counter.IsActive);
        Assert.Equal(createdAt.AddMinutes(2), counter.UpdatedAt);
    }

    [Fact]
    public void RejectsEmptyName()
    {
        Assert.Throws<DomainException>(() => new QueueCounter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", DateTimeOffset.UtcNow));
    }
}
