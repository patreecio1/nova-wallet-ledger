namespace NovaWallet.BuildingBlocks.Application.Paging;

/// <summary>Clamps caller-supplied paging input rather than trusting it — never a raw page/pageSize from the request.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static PageRequest From(int? page, int? pageSize)
    {
        var normalizedPage = page is null or < 1 ? 1 : page.Value;
        var normalizedPageSize = pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };

        return new PageRequest(normalizedPage, normalizedPageSize);
    }

    public int Skip => (Page - 1) * PageSize;
}
