namespace QueueFlow.Domain.Common;

public abstract class BaseEntity
{
    protected BaseEntity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Entity identifier cannot be empty.");
        }

        Id = id;
    }

    public Guid Id { get; private set; }
}
