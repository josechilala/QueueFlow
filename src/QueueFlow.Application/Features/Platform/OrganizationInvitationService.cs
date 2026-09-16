using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Tenants;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Platform;

public sealed record CreateOrganizationInvitationCommand(string Email, string? ResponsibleName, string? OrganizationName, string? Plan);
public sealed record OrganizationInvitationDto(Guid Id, string Email, string? ResponsibleName, string? OrganizationName, string? Plan, InvitationStatus Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? VerifiedAt, DateTimeOffset? UsedAt, DateTimeOffset? RevokedAt, Guid? ActivatedOrganizationId);
public sealed record InvitationIssuedDto(OrganizationInvitationDto Invitation, string ActivationUrl, bool CanSendInvitation = false);
public sealed record PublicInvitationDto(string MaskedEmail, string? OrganizationName, InvitationStatus Status, bool CanRequestVerificationCode, bool IsVerified);
public sealed record VerificationCodeRequestDto(bool Delivered, string? DevelopmentCode);
public sealed record CompleteInvitationActivationCommand(string OrganizationName, string? Slug, string ResponsibleName, string Password, string TimeZone, string? ActivationAuthorization = null);
public sealed record ActivationAuthorizationDto(string ActivationAuthorization, DateTimeOffset ExpiresAt);
public sealed record ActivationCompletedDto(Guid OrganizationId, string Email);

public sealed class OrganizationInvitationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    ITokenService tokens,
    IActivationEmailSender emailSender,
    TenantService tenants,
    IOptions<OnboardingOptions> options)
{
    private readonly OnboardingOptions _options = options.Value;
    private static readonly Error NotFound = new("invitation.not_found", "Convite não encontrado ou indisponível.");
    private static readonly Error Forbidden = new("platform.forbidden", "Acesso administrativo da plataforma é obrigatório.");

    public async Task<Result<InvitationIssuedDto>> CreateAsync(CreateOrganizationInvitationCommand command, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure<InvitationIssuedDto>(Forbidden);
        var email = NormalizeEmail(command.Email);
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)) return Result.Failure<InvitationIssuedDto>(new("invitation.email", "Informe um e-mail válido."));
        if (command.Plan is not null && command.Plan != "Trial") return Result.Failure<InvitationIssuedDto>(new("invitation.plan", "A ativação inicia com o Trial existente."));
        var issued = CreateInvitation(command with { Email = email });
        db.OrganizationInvitations.Add(issued.Invitation);
        Audit("PlatformInvitationCreated", "OrganizationInvitation", issued.Invitation.Id, new { issued.Invitation.Email });
        await db.SaveChangesAsync(ct);
        return Result.Success(new InvitationIssuedDto(Map(issued.Invitation), issued.ActivationUrl, emailSender.IsConfigured && emailSender.SupportsInvitationDelivery));
    }

    public async Task<IReadOnlyList<OrganizationInvitationDto>> ListAsync(CancellationToken ct)
    {
        if (!IsPlatformAdmin()) throw new UnauthorizedAccessException();
        var invitations = await db.OrganizationInvitations.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
        return invitations.Select(Map).ToArray();
    }

    public async Task<Result> RevokeAsync(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure(Forbidden);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var invitation = await LockByIdAsync(id, ct);
        if (invitation is null) return Result.Failure(NotFound);
        invitation.Revoke(clock.UtcNow);
        Audit("PlatformInvitationRevoked", "OrganizationInvitation", invitation.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result<InvitationIssuedDto>> ReissueAsync(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure<InvitationIssuedDto>(Forbidden);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var invitation = await LockByIdAsync(id, ct);
        if (invitation is null || invitation.UsedAt is not null || invitation.RevokedAt is not null) return Result.Failure<InvitationIssuedDto>(NotFound);
        invitation.Revoke(clock.UtcNow);
        var issued = CreateInvitation(new(invitation.Email, invitation.ResponsibleName, invitation.OrganizationName, invitation.Plan));
        db.OrganizationInvitations.Add(issued.Invitation);
        Audit("PlatformInvitationReissued", "OrganizationInvitation", issued.Invitation.Id, new { PreviousInvitationId = invitation.Id, issued.Invitation.Email });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(new InvitationIssuedDto(Map(issued.Invitation), issued.ActivationUrl, emailSender.IsConfigured && emailSender.SupportsInvitationDelivery));
    }

    public async Task<Result> SendInvitationAsync(Guid id, string token, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure(Forbidden);
        if (!emailSender.IsConfigured || !emailSender.SupportsInvitationDelivery) return Result.Failure(new("invitation.email_unavailable", "O envio de convites não está configurado."));
        var invitation = await FindAsync(token, ct);
        if (invitation is null || invitation.Id != id || !invitation.IsUsable(clock.UtcNow)) return Result.Failure(NotFound);
        await emailSender.SendInvitationAsync(invitation.Email, $"{PublicOrigin()}/ativar#{token}", ct);
        Audit("PlatformInvitationSent", "OrganizationInvitation", invitation.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PublicInvitationDto>> GetPublicAsync(string token, CancellationToken ct)
    {
        var invitation = await FindAsync(token, ct);
        if (invitation is null) return Result.Failure<PublicInvitationDto>(NotFound);
        var status = Status(invitation);
        return Result.Success(new PublicInvitationDto(MaskEmail(invitation.Email), invitation.OrganizationName, status, invitation.IsUsable(clock.UtcNow), invitation.VerifiedAt is not null));
    }

    public async Task<Result<VerificationCodeRequestDto>> RequestVerificationCodeAsync(string token, CancellationToken ct)
    {
        if (!emailSender.IsConfigured) return Result.Failure<VerificationCodeRequestDto>(new("invitation.email_unavailable", "O envio de código não está configurado."));
        if (string.IsNullOrWhiteSpace(_options.VerificationCodePepper)) return Result.Failure<VerificationCodeRequestDto>(new("invitation.code_unavailable", "A verificação não está configurada."));
        await using var transaction = await db.BeginTransactionAsync(ct);
        var invitation = await LockByTokenAsync(token, ct);
        if (invitation is null || !invitation.IsUsable(clock.UtcNow)) return Result.Failure<VerificationCodeRequestDto>(NotFound);
        if (invitation.VerificationCodeSentAt is DateTimeOffset sentAt && clock.UtcNow < sentAt.AddSeconds(_options.VerificationCodeCooldownSeconds))
            return Result.Failure<VerificationCodeRequestDto>(new("invitation.cooldown", "Aguarde antes de solicitar um novo código."));
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        invitation.SetVerificationCode(HashCode(invitation.Id, code), clock.UtcNow.AddMinutes(_options.VerificationCodeExpirationMinutes), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await emailSender.SendVerificationCodeAsync(invitation.Email, code, ct);
        Audit("VerificationCodeRequested", "OrganizationInvitation", invitation.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success(new VerificationCodeRequestDto(true, _options.ExposeVerificationCodeForDevelopment ? code : null));
    }

    public async Task<Result<ActivationAuthorizationDto>> VerifyCodeAsync(string token, string code, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var invitation = await LockByTokenAsync(token, ct);
        if (invitation is null || !invitation.IsUsable(clock.UtcNow) || invitation.VerificationCodeHash is null || invitation.VerificationCodeExpiresAt <= clock.UtcNow)
            return Result.Failure<ActivationAuthorizationDto>(NotFound);
        if (invitation.AttemptCount >= _options.VerificationCodeMaxAttempts) return Result.Failure<ActivationAuthorizationDto>(new("invitation.too_many_attempts", "Limite de tentativas atingido. Solicite um novo convite."));
        if (string.IsNullOrWhiteSpace(code) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(invitation.VerificationCodeHash), Encoding.UTF8.GetBytes(HashCode(invitation.Id, code))))
        {
            invitation.RecordVerificationFailure(clock.UtcNow);
            if (invitation.AttemptCount >= _options.VerificationCodeMaxAttempts) invitation.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<ActivationAuthorizationDto>(new("invitation.invalid_code", "Código de verificação inválido."));
        }
        var authorization = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = clock.UtcNow.AddMinutes(10);
        invitation.AuthorizeActivation(tokens.HashToken(authorization), expiresAt, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        Audit("InvitationVerified", "OrganizationInvitation", invitation.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ActivationAuthorizationDto(authorization, expiresAt));
    }

    public async Task<Result<ActivationCompletedDto>> CompleteActivationAsync(string token, CompleteInvitationActivationCommand command, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var invitation = await LockByTokenAsync(token, ct);
        if (invitation is null || !invitation.IsUsable(clock.UtcNow) || invitation.VerifiedAt is null ||
            !ValidToken(command.ActivationAuthorization) || invitation.ActivationAuthorizationHash is null ||
            invitation.ActivationAuthorizationConsumedAt is not null || (invitation.ActivationAuthorizationExpiresAt is not DateTimeOffset authorizationExpiresAt || authorizationExpiresAt <= clock.UtcNow) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(invitation.ActivationAuthorizationHash), Encoding.UTF8.GetBytes(tokens.HashToken(command.ActivationAuthorization!))))
            return Result.Failure<ActivationCompletedDto>(NotFound);
        var slug = string.IsNullOrWhiteSpace(command.Slug) ? ToSlug(command.OrganizationName) : ToSlug(command.Slug);
        var provisioned = await tenants.ProvisionAsync(new(command.OrganizationName, slug, command.TimeZone, command.ResponsibleName, invitation.Email, command.Password), ct);
        if (provisioned.IsFailure) return Result.Failure<ActivationCompletedDto>(provisioned.Error);
        invitation.ConsumeActivationAuthorization(clock.UtcNow);
        invitation.MarkUsed(provisioned.Value.Organization.Id, clock.UtcNow);
        db.PlatformAuditLogs.Add(new PlatformAuditLog(Guid.NewGuid(), null, "OrganizationActivated", "Organization", provisioned.Value.Organization.Id, null, Guid.NewGuid().ToString("N"), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(new ActivationCompletedDto(provisioned.Value.Organization.Id, invitation.Email));
    }

    private (OrganizationInvitation Invitation, string ActivationUrl) CreateInvitation(CreateOrganizationInvitationCommand command)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = clock.UtcNow.AddHours(_options.InvitationExpirationHours);
        var invitation = new OrganizationInvitation(Guid.NewGuid(), command.Email, command.ResponsibleName, command.OrganizationName, command.Plan, tokens.HashToken(rawToken), expiresAt, clock.UtcNow);
        return (invitation, $"{PublicOrigin()}/ativar#{rawToken}");
    }

    private Task<OrganizationInvitation?> LockByIdAsync(Guid id, CancellationToken ct) => db.OrganizationInvitations.FromSqlInterpolated($"SELECT * FROM \"OrganizationInvitations\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    private async Task<OrganizationInvitation?> FindAsync(string token, CancellationToken ct) => ValidToken(token) ? await db.OrganizationInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == tokens.HashToken(token), ct) : null;
    private async Task<OrganizationInvitation?> LockByTokenAsync(string token, CancellationToken ct) => ValidToken(token) ? await db.OrganizationInvitations.FromSqlInterpolated($"SELECT * FROM \"OrganizationInvitations\" WHERE \"TokenHash\" = {tokens.HashToken(token)} FOR UPDATE").SingleOrDefaultAsync(ct) : null;
    private bool IsPlatformAdmin() => currentUser.IsAuthenticated && currentUser.IdentityType == IdentityType.Platform && currentUser.PlatformUserId is not null && currentUser.OrganizationId is null;
    private string HashCode(Guid invitationId, string code) { var pepper = _options.VerificationCodePepper ?? throw new InvalidOperationException("Verification code pepper is not configured."); return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes($"{invitationId:N}:{code}"))); }
    private InvitationStatus Status(OrganizationInvitation invitation) => invitation.UsedAt is not null ? InvitationStatus.Used : invitation.RevokedAt is not null ? InvitationStatus.Revoked : invitation.IsExpired(clock.UtcNow) ? InvitationStatus.Expired : invitation.VerifiedAt is not null ? InvitationStatus.Verified : InvitationStatus.Pending;
    private OrganizationInvitationDto Map(OrganizationInvitation invitation) => new(invitation.Id, invitation.Email, invitation.ResponsibleName, invitation.OrganizationName, invitation.Plan, Status(invitation), invitation.CreatedAt, invitation.ExpiresAt, invitation.VerifiedAt, invitation.UsedAt, invitation.RevokedAt, invitation.ActivatedOrganizationId);
    private string PublicOrigin() { var raw = _options.PublicUrl?.Trim().TrimEnd('/'); var localhost = Uri.TryCreate(raw, UriKind.Absolute, out var localUri) && (localUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || localUri.Host == "127.0.0.1"); if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !(localhost && _options.AllowLocalPublicUrl)) || (localhost && !_options.AllowLocalPublicUrl) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new InvalidOperationException("Onboarding public URL must be an HTTPS URL outside local development."); return raw!; }
    private void Audit(string action, string resourceType, Guid resourceId, object? data = null) => db.PlatformAuditLogs.Add(new PlatformAuditLog(Guid.NewGuid(), currentUser.PlatformUserId, action, resourceType, resourceId, data is null ? null : System.Text.Json.JsonSerializer.Serialize(data), Guid.NewGuid().ToString("N"), clock.UtcNow));
    private static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
    private static string MaskEmail(string email) { var at = email.IndexOf('@'); return at <= 1 ? "***" : $"{email[..2]}***{email[at..]}"; }
    private static string ToSlug(string value) { var normalized = string.Concat(value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)); var slug = System.Text.RegularExpressions.Regex.Replace(normalized, "[^a-z0-9]+", "-").Trim('-'); if (slug.Length is < 3 or > 100) throw new QueueFlow.Domain.Common.DomainException("Organization slug is invalid."); return slug; }
}
