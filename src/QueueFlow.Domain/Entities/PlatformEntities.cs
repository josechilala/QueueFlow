using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

public sealed class PlatformUser : AuditableEntity
{
    private PlatformUser() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public PlatformUser(Guid id, string name, string email, string passwordHash, DateTimeOffset now) : base(id, now)
    {
        Name = Required(name, nameof(name), 200);
        Email = Required(email, nameof(email), 320).ToLowerInvariant();
        PasswordHash = Required(passwordHash, nameof(passwordHash), 2000);
    }

    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public void SetPasswordHash(string passwordHash, DateTimeOffset now) { PasswordHash = Required(passwordHash, nameof(passwordHash), 2000); MarkUpdated(now); }
    public void SetActive(bool active, DateTimeOffset now) { IsActive = active; MarkUpdated(now); }
    private static string Required(string value, string name, int maximum) { var normalized = value?.Trim() ?? string.Empty; if (normalized.Length == 0 || normalized.Length > maximum) throw new DomainException($"{name} is required."); return normalized; }
}

public sealed class PlatformRefreshToken : AuditableEntity
{
    private PlatformRefreshToken() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public PlatformRefreshToken(Guid id, Guid platformUserId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now) : base(id, now)
    {
        if (platformUserId == Guid.Empty || string.IsNullOrWhiteSpace(tokenHash)) throw new DomainException("Platform refresh token is invalid.");
        PlatformUserId = platformUserId; TokenHash = tokenHash; ExpiresAt = expiresAt;
    }
    public Guid PlatformUserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) { if (RevokedAt is null) RevokedAt = now; }
}

public sealed class OrganizationInvitation : AuditableEntity
{
    private OrganizationInvitation() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public OrganizationInvitation(Guid id, string email, string? responsibleName, string? organizationName, string? plan, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now) : base(id, now)
    {
        Email = Required(email, nameof(email), 320).ToLowerInvariant();
        ResponsibleName = Optional(responsibleName, 200);
        OrganizationName = Optional(organizationName, 200);
        Plan = Optional(plan, 100);
        TokenHash = Required(tokenHash, nameof(tokenHash), 200);
        ExpiresAt = expiresAt > now ? expiresAt : throw new DomainException("Invitation expiration is invalid.");
    }

    public string Email { get; private set; } = string.Empty;
    public string? ResponsibleName { get; private set; }
    public string? OrganizationName { get; private set; }
    public string? Plan { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? VerificationCodeHash { get; private set; }
    public DateTimeOffset? VerificationCodeExpiresAt { get; private set; }
    public DateTimeOffset? VerificationCodeSentAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public Guid? ActivatedOrganizationId { get; private set; }
    public string? ActivationAuthorizationHash { get; private set; }
    public DateTimeOffset? ActivationAuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? ActivationAuthorizationConsumedAt { get; private set; }

    public void AuthorizeActivation(string hash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        MarkVerified(now);
        ActivationAuthorizationHash = Required(hash, nameof(hash), 200);
        ActivationAuthorizationExpiresAt = expiresAt > now ? expiresAt : throw new DomainException("Activation authorization expiration is invalid.");
        ActivationAuthorizationConsumedAt = null;
        VerificationCodeHash = null;
    }

    public void ConsumeActivationAuthorization(DateTimeOffset now)
    {
        if (!IsUsable(now) || ActivationAuthorizationHash is null || ActivationAuthorizationExpiresAt <= now || ActivationAuthorizationConsumedAt is not null)
            throw new DomainException("Activation authorization is unavailable.");
        ActivationAuthorizationConsumedAt = now;
        ActivationAuthorizationHash = null;
        MarkUpdated(now);
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && !IsExpired(now);
    public void SetVerificationCode(string hash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (!IsUsable(now) || expiresAt <= now || string.IsNullOrWhiteSpace(hash)) throw new DomainException("Invitation verification code is invalid.");
        ActivationAuthorizationHash = null;
        ActivationAuthorizationExpiresAt = null;
        VerificationCodeHash = hash; VerificationCodeExpiresAt = expiresAt; VerificationCodeSentAt = now; VerifiedAt = null; AttemptCount = 0; LastAttemptAt = null; MarkUpdated(now);
    }
    public void RecordVerificationFailure(DateTimeOffset now) { if (!IsUsable(now)) throw new DomainException("Invitation is unavailable."); AttemptCount++; LastAttemptAt = now; MarkUpdated(now); }
    public void MarkVerified(DateTimeOffset now) { if (!IsUsable(now) || VerificationCodeHash is null || VerificationCodeExpiresAt <= now) throw new DomainException("Invitation verification is invalid."); VerifiedAt = now; MarkUpdated(now); }
    public void MarkUsed(Guid organizationId, DateTimeOffset now) { if (!IsUsable(now) || VerifiedAt is null || organizationId == Guid.Empty) throw new DomainException("Invitation activation is invalid."); UsedAt = now; ActivatedOrganizationId = organizationId; VerificationCodeHash = null; MarkUpdated(now); }
    public void Revoke(DateTimeOffset now) { if (UsedAt is null && RevokedAt is null) { RevokedAt = now; VerificationCodeHash = null; MarkUpdated(now); } }
    private static string Required(string value, string name, int maximum) { var normalized = value?.Trim() ?? string.Empty; if (normalized.Length == 0 || normalized.Length > maximum) throw new DomainException($"{name} is required."); return normalized; }
    private static string? Optional(string? value, int maximum) { var normalized = value?.Trim(); return string.IsNullOrEmpty(normalized) ? null : normalized.Length > maximum ? throw new DomainException("Invitation text is too long.") : normalized; }
}

public sealed class PlatformAuditLog : AuditableEntity
{
    private PlatformAuditLog() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public PlatformAuditLog(Guid id, Guid? platformUserId, string action, string resourceType, Guid? resourceId, string? data, string correlationId, DateTimeOffset now) : base(id, now)
    {
        Action = Required(action, 200); ResourceType = Required(resourceType, 200); ResourceId = resourceId; PlatformUserId = platformUserId; Data = data; CorrelationId = Required(correlationId, 200);
    }
    public Guid? PlatformUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? Data { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    private static string Required(string value, int maximum) { var normalized = value?.Trim() ?? string.Empty; if (normalized.Length == 0 || normalized.Length > maximum) throw new DomainException("Platform audit data is invalid."); return normalized; }
}
