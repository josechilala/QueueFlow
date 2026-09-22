using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Platform;

public sealed record CreateTrialRequestCommand(string? Name, string? Email, string? CompanyName, string? Phone, bool AcceptedTerms);
public sealed record TrialRequestDto(Guid Id, string Name, string Email, string CompanyName, string Phone, TrialRequestStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset AcceptedTermsAt, string TermsVersion, DateTimeOffset? DecidedAt, Guid? DecidedByPlatformUserId, Guid? InvitationId);

public sealed class TrialRequestService(IApplicationDbContext db, ICurrentUser user, IClock clock,
    OrganizationInvitationService invitations, IActivationEmailSender emailSender)
{
    private static readonly Error Forbidden = new("platform.forbidden", "Acesso administrativo da plataforma é obrigatório.");

    public async Task<Result> CreateAsync(CreateTrialRequestCommand command, CancellationToken ct)
    {
        var email = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = command.Phone?.Trim() ?? string.Empty;
        if (!ValidText(command.Name, 200) || !ValidText(command.CompanyName, 200))
            return Result.Failure(new("trial_request.fields", "Informe nome e empresa entre 2 e 200 caracteres."));
        if (email.Length > 320 || email.Any(char.IsControl) || !MailAddress.TryCreate(email, out var address) || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(new("trial_request.email", "Informe um e-mail válido."));
        if (phone.Length > 30 || phone.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '(' or ')' or '-' or '.')) || phone.Count(char.IsAsciiDigit) is < 8 or > 15 || phone.Count(c => c == '+') > 1 || (phone.Contains('+') && !phone.StartsWith('+')))
            return Result.Failure(new("trial_request.phone", "Informe um telefone válido com DDD, entre 8 e 15 dígitos."));
        if (!command.AcceptedTerms) return Result.Failure(new("trial_request.consent", "É necessário aceitar os Termos de Uso e a Política de Privacidade."));
        phone = (phone.StartsWith('+') ? "+" : "") + string.Concat(phone.Where(char.IsAsciiDigit));

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockTrialRequestEmailAsync(email, ct);
        // A retry never reveals or overwrites an existing request, irrespective of its decision.
        if (!await db.TrialRequests.AnyAsync(x => x.Email == email, ct))
        {
            db.TrialRequests.Add(new TrialRequest(Guid.NewGuid(), command.Name!, email, command.CompanyName!, phone, clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<TrialRequestDto>> ListAsync(CancellationToken ct)
    {
        if (!IsPlatformAdmin()) throw new UnauthorizedAccessException();
        return (await db.TrialRequests.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)).Select(Map).ToArray();
    }

    public async Task<Result<TrialRequestDto>> GetAsync(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure<TrialRequestDto>(Forbidden);
        var request = await db.TrialRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return request is null ? Result.Failure<TrialRequestDto>(new("trial_request.not_found", "Solicitação não encontrada.")) : Result.Success(Map(request));
    }

    public async Task<Result<TrialRequestDto>> DecideAsync(Guid id, bool approve, CancellationToken ct)
    {
        if (!IsPlatformAdmin()) return Result.Failure<TrialRequestDto>(Forbidden);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var request = await db.TrialRequests.FromSqlInterpolated($"SELECT * FROM \"TrialRequests\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (request is null) return Result.Failure<TrialRequestDto>(new("trial_request.not_found", "Solicitação não encontrada."));
        if (request.Status != TrialRequestStatus.Pending) return Result.Failure<TrialRequestDto>(new("trial_request.decided", "Esta solicitação já foi analisada. Atualize a lista."));
        Guid? invitationId = null;
        if (approve)
        {
            if (!emailSender.IsConfigured || !emailSender.SupportsInvitationDelivery)
                return Result.Failure<TrialRequestDto>(new("invitation.email_unavailable", "O envio de convites não está configurado. A solicitação continua pendente."));
            var issued = await invitations.CreateAsync(new(request.Email, request.Name, request.CompanyName, "Trial"), ct);
            if (issued.IsFailure) return Result.Failure<TrialRequestDto>(issued.Error);
            invitationId = issued.Value.Invitation.Id;
            var token = new Uri(issued.Value.ActivationUrl).Fragment[1..];
            try
            {
                var sent = await invitations.SendInvitationAsync(invitationId.Value, token, ct);
                if (sent.IsFailure) return Result.Failure<TrialRequestDto>(sent.Error);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // All database writes, including the invitation, roll back. No token is persisted in plaintext.
                return Result.Failure<TrialRequestDto>(new("trial_request.delivery_failed", "Não foi possível confirmar o envio do convite. A solicitação continua pendente; tente novamente."));
            }
        }
        request.Decide(user.PlatformUserId!.Value, invitationId, clock.UtcNow);
        db.PlatformAuditLogs.Add(new PlatformAuditLog(Guid.NewGuid(), user.PlatformUserId,
            approve ? "PlatformTrialRequestApproved" : "PlatformTrialRequestRejected", "TrialRequest", request.Id,
            invitationId.HasValue ? System.Text.Json.JsonSerializer.Serialize(new { InvitationId = invitationId }) : null,
            Guid.NewGuid().ToString("N"), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(request));
    }

    private bool IsPlatformAdmin() => user.IsAuthenticated && user.IdentityType == IdentityType.Platform && user.PlatformUserId is not null && user.OrganizationId is null;
    private static bool ValidText(string? value, int max) => value?.Trim() is { Length: >= 2 } text && text.Length <= max && !text.Any(char.IsControl);
    private static TrialRequestDto Map(TrialRequest r) => new(r.Id, r.Name, r.Email, r.CompanyName, r.Phone, r.Status, r.CreatedAt, r.AcceptedTermsAt, r.TermsVersion, r.DecidedAt, r.DecidedByPlatformUserId, r.InvitationId);
}
