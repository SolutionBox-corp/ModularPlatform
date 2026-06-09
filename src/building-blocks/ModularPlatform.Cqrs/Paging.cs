namespace ModularPlatform.Cqrs;

/// <summary>
/// A page of results. The standard envelope for list queries — return this from the handler (wrapped in
/// <c>ApiResponse</c> at the endpoint). Never return an unbounded list from a list endpoint.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// A validated paging request. Clamps to sane bounds (1-based page; page size 1..<see cref="MaxPageSize"/>,
/// default <see cref="DefaultPageSize"/>) so a caller can never request an unbounded or negative page.
/// </summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int? page, int? pageSize)
    {
        Page = page is > 0 ? page.Value : 1;
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
    }

    public int Page { get; }
    public int PageSize { get; }
    public int Skip => (Page - 1) * PageSize;
}
