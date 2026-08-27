using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;
using System.Security.Cryptography;

namespace QueueFlow.Domain.Entities;

public sealed class Organization : AuditableEntity
{
    private Organization() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Organization(Guid id, string name, string slug, string timeZone, DateTimeOffset now) : base(id, now)
    {
        Name = Required(name, nameof(name)); Slug = Required(slug, nameof(slug)).ToLowerInvariant(); TimeZone = Required(timeZone, nameof(timeZone));
    }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Document { get; private set; }
    public string TimeZone { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{name} is required.") : value.Trim();
}

public sealed class Branch : AuditableEntity, ITenantEntity
{
    private Branch() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Branch(Guid id, Guid organizationId, string name, string timeZone, DateTimeOffset now)
        : this(id, organizationId, name, null, timeZone, now) { }
    public Branch(Guid id, Guid organizationId, string name, string? address, string timeZone, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty) throw new DomainException("A valid organization is required.");
        OrganizationId = organizationId;
        PublicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        ApplyDetails(name, address, timeZone);
    }
    public Guid OrganizationId { get; private set; }
    public string PublicId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TimeZone { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string name, string? address, string timeZone, DateTimeOffset now)
    {
        ApplyDetails(name, address, timeZone);
        MarkUpdated(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive) return;
        IsActive = isActive;
        MarkUpdated(now);
    }

    private void ApplyDetails(string name, string? address, string timeZone)
    {
        Name = Required(name, "Branch name", 200);
        TimeZone = Required(timeZone, "Time zone", 100);
        Address = Optional(address, "Address", 500);
    }

    private static string Required(string value, string field, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength) throw new DomainException($"{field} must contain between 1 and {maximumLength} characters.");
        return normalized;
    }

    private static string? Optional(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximumLength) throw new DomainException($"{field} must contain at most {maximumLength} characters.");
        return normalized;
    }
}

public sealed class AppUser : AuditableEntity, ITenantEntity
{
    private AppUser() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public AppUser(Guid id, Guid organizationId, string name, string email, string passwordHash, UserRole role, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; Name = name.Trim(); Email = email.Trim().ToLowerInvariant(); PasswordHash = passwordHash; Role = role; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string name, UserRole role, DateTimeOffset now)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 2 or > 200) throw new DomainException("User name must contain between 2 and 200 characters.");
        Name = normalizedName;
        Role = role;
        MarkUpdated(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive) return;
        IsActive = isActive;
        MarkUpdated(now);
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        PasswordHash = passwordHash;
        MarkUpdated(now);
    }
}

public sealed class UserBranch : BaseEntity, ITenantEntity
{
    private UserBranch() : base(Guid.NewGuid()) { }
    public UserBranch(Guid id, Guid organizationId, Guid userId, Guid branchId, UserRole role) : base(id)
    { OrganizationId = organizationId; UserId = userId; BranchId = branchId; Role = role; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public UserRole Role { get; private set; }
}

public sealed class RefreshToken : AuditableEntity, ITenantEntity
{
    private RefreshToken() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public RefreshToken(Guid id, Guid organizationId, Guid userId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; UserId = userId; TokenHash = tokenHash; ExpiresAt = expiresAt; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}

public sealed class Subscription : AuditableEntity, ITenantEntity
{
    private Subscription() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Subscription(Guid id, Guid organizationId, string plan, DateTimeOffset trialEndsAt, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; Plan = plan; TrialEndsAt = trialEndsAt; }
    public Guid OrganizationId { get; private set; }
    public string Plan { get; private set; } = "Trial";
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Trial;
    public DateTimeOffset TrialEndsAt { get; private set; }
}
