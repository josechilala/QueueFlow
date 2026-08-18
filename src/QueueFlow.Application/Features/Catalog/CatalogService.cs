using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Application.Features.Catalog;

public sealed record BranchDto(Guid Id, string Name, string TimeZone, bool IsActive);
public sealed record ServiceDto(Guid Id, Guid BranchId, string Name, string Prefix, int AverageDurationMinutes, bool IsActive);
public sealed record CounterDto(Guid Id, Guid BranchId, string Name, bool IsActive);

public sealed class CatalogService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException("Tenant context is required.");
    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken ct) => await db.Branches.AsNoTracking().OrderBy(x => x.Name).Select(x => new BranchDto(x.Id, x.Name, x.TimeZone, x.IsActive)).ToListAsync(ct);
    public async Task<BranchDto> CreateBranchAsync(string name, string timeZone, CancellationToken ct) { var value = new Branch(Guid.NewGuid(), Tenant, name, timeZone, clock.UtcNow); db.Branches.Add(value); await db.SaveChangesAsync(ct); return new(value.Id, value.Name, value.TimeZone, value.IsActive); }
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct) => await db.Services.AsNoTracking().OrderBy(x => x.Name).Select(x => new ServiceDto(x.Id, x.BranchId, x.Name, x.Prefix, x.AverageDurationMinutes, x.IsActive)).ToListAsync(ct);
    public async Task<Result<ServiceDto>> CreateServiceAsync(Guid branchId, string name, string prefix, int minutes, CancellationToken ct) { if (!await db.Branches.AnyAsync(x => x.Id == branchId, ct)) return Result.Failure<ServiceDto>(new("catalog.branch_not_found", "Branch was not found in the current tenant.")); var value = new Service(Guid.NewGuid(), Tenant, branchId, name, prefix, minutes, clock.UtcNow); db.Services.Add(value); await db.SaveChangesAsync(ct); return Result.Success(new ServiceDto(value.Id, value.BranchId, value.Name, value.Prefix, value.AverageDurationMinutes, value.IsActive)); }
    public async Task<CounterDto> CreateCounterAsync(Guid branchId, string name, CancellationToken ct) { var value = new QueueCounter(Guid.NewGuid(), Tenant, branchId, name, clock.UtcNow); db.QueueCounters.Add(value); await db.SaveChangesAsync(ct); return new(value.Id, value.BranchId, value.Name, value.IsActive); }
}
