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

    [Fact]
    public void CompletedTicketCannotReturnToAnEarlierState()
    {
        var ticket = Create(); var now = DateTimeOffset.UtcNow;
        ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now); ticket.Start(now); ticket.Complete(now);

        Assert.Throws<DomainException>(() => ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now));
        Assert.Throws<DomainException>(() => ticket.Start(now));
        Assert.Throws<DomainException>(() => ticket.Cancel(now));
        Assert.Throws<DomainException>(() => ticket.MarkNoShow(now));
    }

    [Fact]
    public void NoShowIsTerminalForCommonTransitions()
    {
        var ticket = Create(); var now = DateTimeOffset.UtcNow;
        ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now); ticket.MarkNoShow(now);

        Assert.Throws<DomainException>(() => ticket.Start(now));
        Assert.Throws<DomainException>(() => ticket.Complete(now));
        Assert.Throws<DomainException>(() => ticket.Cancel(now));
    }

    [Theory]
    [InlineData(0, "A001", "token")]
    [InlineData(1, "", "token")]
    [InlineData(1, "A001", "")]
    public void InvalidIssueDataIsRejected(long sequence, string number, string token)
    {
        Assert.Throws<DomainException>(() => new QueueTicket(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            number, sequence, TicketPriority.Normal, token, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InvalidPriorityIsRejected()
    {
        Assert.Throws<DomainException>(() => new QueueTicket(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "A001", 1, (TicketPriority)999, "token", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ClosedTicketCanBeAnonymizedAndPublicTokenIsRotated()
    {
        var ticket = Create(); var now = DateTimeOffset.UtcNow; var oldToken = ticket.CustomerPublicToken;
        ticket.Call(Guid.NewGuid(), Guid.NewGuid(), now); ticket.Start(now); ticket.Complete(now); ticket.Anonymize("anonymous-token", now.AddDays(90));
        Assert.Equal("anonymous-token", ticket.CustomerPublicToken); Assert.NotEqual(oldToken, ticket.CustomerPublicToken); Assert.Equal(now.AddDays(90), ticket.AnonymizedAt);
    }

    private static QueueTicket Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A001", 1, TicketPriority.Normal, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
}
