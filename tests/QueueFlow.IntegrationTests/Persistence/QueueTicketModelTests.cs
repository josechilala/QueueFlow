using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class QueueTicketModelTests
{
    [Fact]
    public void TicketSequenceAndPublicTokenAreUniqueInDatabaseModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var db = new ApplicationDbContext(options, new AnonymousCurrentUser());
        var entity = db.Model.FindEntityType("QueueFlow.Domain.Entities.QueueTicket")!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(["QueueId", "SequenceNumber"]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(["CustomerPublicToken"]));
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public QueueFlow.Domain.Enums.UserRole? Role => null;
        public bool IsAuthenticated => false;
    }
}
