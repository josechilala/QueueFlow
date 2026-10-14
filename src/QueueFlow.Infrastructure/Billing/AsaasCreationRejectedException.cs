using System.Net;

namespace QueueFlow.Infrastructure.Billing;

/// <summary>An explicit validation/authentication rejection of a creation request, never a read failure.</summary>
internal sealed class AsaasCreationRejectedException(HttpStatusCode status)
    : HttpRequestException($"Asaas sandbox creation rejected ({(int)status}).", null, status);
