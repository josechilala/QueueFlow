using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class ServiceTests
{
    [Fact]
    public void CreatesActiveServiceWithNormalizedValues()
    {
        var service = new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Consulta ", " Atendimento clínico ", " c ", 20, DateTimeOffset.UtcNow);

        Assert.Equal("Consulta", service.Name);
        Assert.Equal("Atendimento clínico", service.Description);
        Assert.Equal("C", service.Prefix);
        Assert.Equal(20, service.AverageDurationMinutes);
        Assert.True(service.IsActive);
    }

    [Theory]
    [InlineData("", "C", 20)]
    [InlineData("Consulta", "", 20)]
    [InlineData("Consulta", "C", 0)]
    [InlineData("Consulta", "C", 1441)]
    public void RejectsInvalidService(string name, string prefix, int duration)
    {
        Assert.Throws<DomainException>(() => new Service(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), name, null, prefix, duration, DateTimeOffset.UtcNow));
    }
}
