using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Error is the normative contract name in the QueueFlow specification and is idiomatic in the C#-only Application boundary.",
    Scope = "type",
    Target = "~T:QueueFlow.Application.Common.Error")]
