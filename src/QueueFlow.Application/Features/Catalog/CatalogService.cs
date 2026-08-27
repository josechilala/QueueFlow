using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Catalog;

public sealed record BranchDto(Guid Id, string PublicId, string Name, string TimeZone, bool IsActive, string? Address);
public sealed record ServiceDto(Guid Id, Guid BranchId, string PublicId, string Name, string? Description, string Prefix, int AverageDurationMinutes, bool IsActive, ServiceAttendanceMode AttendanceMode);
public sealed record CounterDto(Guid Id, Guid BranchId, string Name, bool IsActive);

public sealed class CatalogService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException("Tenant context is required.");
    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken ct) => await db.Branches.AsNoTracking().OrderBy(x => x.Name).Select(x => new BranchDto(x.Id, x.PublicId, x.Name, x.TimeZone, x.IsActive, x.Address)).ToListAsync(ct);
    public async Task<Result<BranchDto>> GetBranchAsync(Guid id, CancellationToken ct)
    {
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return branch is null ? BranchNotFound() : Result.Success(ToDto(branch));
    }
    public async Task<BranchDto> CreateBranchAsync(string name, string? address, string timeZone, CancellationToken ct)
    {
        ValidateTimeZone(timeZone);
        var value = new Branch(Guid.NewGuid(), Tenant, name, address, timeZone, clock.UtcNow);
        db.Branches.Add(value);
        await db.SaveChangesAsync(ct);
        return ToDto(value);
    }
    public async Task<Result<BranchDto>> UpdateBranchAsync(Guid id, string name, string? address, string timeZone, CancellationToken ct)
    {
        ValidateTimeZone(timeZone);
        var branch = await db.Branches.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (branch is null) return BranchNotFound();
        branch.Update(name, address, timeZone, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(branch));
    }
    public async Task<Result<BranchDto>> SetBranchActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (branch is null) return BranchNotFound();
        branch.SetActive(isActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(branch));
    }
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct) => await db.Services.AsNoTracking().OrderBy(x => x.Name).Select(x => new ServiceDto(x.Id, x.BranchId, x.PublicId, x.Name, x.Description, x.Prefix, x.AverageDurationMinutes, x.IsActive, x.AttendanceMode)).ToListAsync(ct);
    public async Task<Result<ServiceDto>> CreateServiceAsync(Guid branchId, string name, string? description, string prefix, int minutes, CancellationToken ct)
    {
        if (!await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct))
            return Result.Failure<ServiceDto>(new("catalog.branch_not_found", "A unidade não foi encontrada ou está inativa."));
        var value = new Service(Guid.NewGuid(), Tenant, branchId, name, description, prefix, minutes, clock.UtcNow);
        db.Services.Add(value);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ServiceDto(value.Id, value.BranchId, value.PublicId, value.Name, value.Description, value.Prefix, value.AverageDurationMinutes, value.IsActive, value.AttendanceMode));
    }
    public async Task<IReadOnlyList<CounterDto>> GetCountersAsync(CancellationToken ct) => await db.QueueCounters.AsNoTracking().OrderBy(x => x.Name).Select(x => new CounterDto(x.Id, x.BranchId, x.Name, x.IsActive)).ToListAsync(ct);
    public async Task<Result<CounterDto>> GetCounterAsync(Guid id, CancellationToken ct)
    {
        var counter = await db.QueueCounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return counter is null ? CounterNotFound() : Result.Success(ToDto(counter));
    }
    public async Task<Result<CounterDto>> CreateCounterAsync(Guid branchId, string name, CancellationToken ct)
    {
        if (!await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct))
            return Result.Failure<CounterDto>(new("catalog.branch_not_found", "A unidade não foi encontrada ou está inativa."));
        var value = new QueueCounter(Guid.NewGuid(), Tenant, branchId, name, clock.UtcNow);
        db.QueueCounters.Add(value);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(value));
    }
    public async Task<Result<CounterDto>> UpdateCounterAsync(Guid id, string name, CancellationToken ct)
    {
        var counter = await db.QueueCounters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (counter is null) return CounterNotFound();
        counter.Update(name, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(counter));
    }
    public async Task<Result<CounterDto>> SetCounterActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var counter = await db.QueueCounters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (counter is null) return CounterNotFound();
        counter.SetActive(isActive, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(counter));
    }

    private static BranchDto ToDto(Branch branch) => new(branch.Id, branch.PublicId, branch.Name, branch.TimeZone, branch.IsActive, branch.Address);
    private static Result<BranchDto> BranchNotFound() => Result.Failure<BranchDto>(new("catalog.branch_not_found", "Branch was not found in the current tenant."));
    private static CounterDto ToDto(QueueCounter counter) => new(counter.Id, counter.BranchId, counter.Name, counter.IsActive);
    private static Result<CounterDto> CounterNotFound() => Result.Failure<CounterDto>(new("catalog.counter_not_found", "O ponto de atendimento não foi encontrado."));
    private static void ValidateTimeZone(string timeZone)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim()); }
        catch (TimeZoneNotFoundException) { throw new QueueFlow.Domain.Common.DomainException("Time zone is invalid."); }
        catch (InvalidTimeZoneException) { throw new QueueFlow.Domain.Common.DomainException("Time zone is invalid."); }
    }
}
