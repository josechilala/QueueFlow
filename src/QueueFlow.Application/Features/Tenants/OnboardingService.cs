using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Tenants;

public sealed record OnboardingProgress(bool Completed, bool BranchReady, bool ServicesReady, bool OperationReady, string NextStep);

public sealed class OnboardingService(IApplicationDbContext db, ICurrentUser user, IClock clock)
{
    public async Task<OnboardingProgress> GetAsync(CancellationToken ct)
    {
        RequireOwner();
        var organization = await db.Organizations.AsNoTracking().SingleAsync(x => x.Id == user.OrganizationId, ct);
        var branches = await db.Branches.Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
        var services = await db.Services.Where(x => x.IsActive && branches.Contains(x.BranchId)).ToListAsync(ct);
        var queues = await db.Queues.Where(x => x.IsActive).Select(x => new { x.ServiceId, x.BranchId }).ToListAsync(ct);
        var settings = await db.ServiceSchedulingSettings.Where(x => x.IsActive).Select(x => x.ServiceId).ToListAsync(ct);
        var schedules = await db.ServiceSchedules.Where(x => x.IsActive).Select(x => new { x.ServiceId, x.BranchId }).ToListAsync(ct);
        var operation = services.Count > 0 && services.All(service =>
            (service.AttendanceMode == ServiceAttendanceMode.AppointmentOnly || queues.Any(x => x.ServiceId == service.Id && x.BranchId == service.BranchId)) &&
            (service.AttendanceMode == ServiceAttendanceMode.QueueOnly || (settings.Contains(service.Id) && schedules.Any(x => x.ServiceId == service.Id && x.BranchId == service.BranchId))));
        var completed = organization.OnboardingCompletedAt is not null;
        return new(completed, branches.Count > 0, services.Count > 0, operation,
            completed ? "dashboard" : branches.Count == 0 ? "branch" : services.Count == 0 ? "services" : !operation ? "operation" : "links");
    }

    public async Task<Result> CompleteAsync(CancellationToken ct)
    {
        var progress = await GetAsync(ct);
        // Completion is a durable milestone, not a recurring operational health check.
        if (progress.Completed) return Result.Success();
        if (!progress.BranchReady || !progress.ServicesReady || !progress.OperationReady)
            return Result.Failure(new("onboarding.incomplete", "Configure uma unidade, serviços e suas filas ou agendas antes de concluir."));
        var organization = await db.Organizations.SingleAsync(x => x.Id == user.OrganizationId, ct);
        organization.CompleteOnboarding(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private void RequireOwner()
    {
        if (user.IdentityType != IdentityType.Tenant || user.OrganizationId is null || user.Role != UserRole.Owner)
            throw new UnauthorizedAccessException();
    }
}
