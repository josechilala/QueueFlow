namespace QueueFlow.Application.Common;

public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
