namespace Agora.Application.Pagination;

/// <summary>One page of results plus the token that fetches the next one.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;
}
