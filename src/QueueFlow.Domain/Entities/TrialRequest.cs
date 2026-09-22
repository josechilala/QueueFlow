using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

public enum TrialRequestStatus { Pending, Approved, Rejected }

public sealed class TrialRequest : AuditableEntity
{
    private TrialRequest() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public TrialRequest(Guid id, string name, string email, string companyName, string phone, DateTimeOffset now) : base(id, now)
    {
        Name = Required(name, 200); Email = Required(email, 320).ToLowerInvariant();
        CompanyName = Required(companyName, 200); Phone = Required(phone, 30);
        AcceptedTermsAt = now;
    }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string CompanyName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public DateTimeOffset AcceptedTermsAt { get; private set; }
    public string TermsVersion { get; private set; } = "2026-09-22";
    public TrialRequestStatus Status { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedByPlatformUserId { get; private set; }
    public Guid? InvitationId { get; private set; }

    public void Decide(Guid administrator, Guid? invitationId, DateTimeOffset now)
    {
        if (Status != TrialRequestStatus.Pending || administrator == Guid.Empty || invitationId == Guid.Empty)
            throw new DomainException("Trial request decision is invalid.");
        Status = invitationId.HasValue ? TrialRequestStatus.Approved : TrialRequestStatus.Rejected;
        InvitationId = invitationId; DecidedAt = now; DecidedByPlatformUserId = administrator; MarkUpdated(now);
    }
    private static string Required(string value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > max || text.Any(char.IsControl)) throw new DomainException("Trial request field is invalid.");
        return text;
    }
}
