using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class QueueTicketTests
{
    [Fact]
    public void CompleteRequiresTicketInService()
    {
        var ticket = Create();
        Assert.Throws<DomainException>(() => ticket.Complete(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ValidLifecycleEndsCompleted()
    {
        var ticket = Create(); var now = DateTimeOffset.UtcNow;
        ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now); ticket.Start(now.AddMinutes(1)); ticket.Complete(now.AddMinutes(3));
        Assert.Equal(TicketStatus.Completed, ticket.Status);
    }

    [Fact]
    public void CompletedTicketCannotBeCompletedAgain()
    {
        var ticket = Create(); var now = DateTimeOffset.UtcNow;
        ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now); ticket.Start(now); ticket.Complete(now);
        Assert.Throws<DomainException>(() => ticket.Complete(now));
    }

    private static QueueTicket Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A001", 1, TicketPriority.Normal, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
}
