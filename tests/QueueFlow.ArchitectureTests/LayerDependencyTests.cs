using QueueFlow.Application.Common;
using QueueFlow.Domain.Common;

namespace QueueFlow.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly string[] ForbiddenDomainDependencies =
    [
        "QueueFlow.Application",
        "QueueFlow.Infrastructure",
        "QueueFlow.Api",
    ];

    private static readonly string[] ForbiddenApplicationDependencies =
    [
        "QueueFlow.Infrastructure",
        "QueueFlow.Api",
    ];

    [Fact]
    public void DomainDoesNotDependOnOuterLayers()
    {
        var references = typeof(BaseEntity).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.DoesNotContain(references, ForbiddenDomainDependencies.Contains);
    }

    [Fact]
    public void ApplicationDoesNotDependOnOuterLayers()
    {
        var references = typeof(Result).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.DoesNotContain(references, ForbiddenApplicationDependencies.Contains);
    }
}
