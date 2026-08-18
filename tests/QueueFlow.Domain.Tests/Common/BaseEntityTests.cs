using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Tests.Common;

public sealed class BaseEntityTests
{
    [Fact]
    public void ConstructorWhenIdentifierIsEmptyThrowsDomainException()
    {
        var action = () => new TestEntity(Guid.Empty);

        Assert.Throws<DomainException>(action);
    }

    private sealed class TestEntity(Guid id) : BaseEntity(id);
}
