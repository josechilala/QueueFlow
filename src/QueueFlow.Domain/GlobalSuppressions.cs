using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Queue is the normative ubiquitous-language entity name in the QueueFlow specification.",
    Scope = "type",
    Target = "~T:QueueFlow.Domain.Entities.Queue")]
