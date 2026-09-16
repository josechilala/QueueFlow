namespace QueueFlow.Application.Common;

public sealed class ConflictException(string message) : Exception(message);
