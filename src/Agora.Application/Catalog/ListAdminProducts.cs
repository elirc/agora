using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>
/// The admin catalog list: the same newest-first cursor page as the storefront, but scoped to a
/// caller-chosen archive <see cref="ProductStatusFilter"/> (default: all). Store-admin only.
/// </summary>
// 🔍 note what makes this the ADMIN list and not the storefront one: the status filter defaults to
// All (archived rows included) and it is behind RequireRole. Same repository, same pagination — the
// difference is entirely "which rows, for whom", which is exactly the soft-archive lesson.
public sealed class ListAdminProducts(IProductRepository products, ICurrentUser currentUser)
{
    public async Task<Page<Product>> ExecuteAsync(
        ProductStatusFilter status,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        int pageSize = limit ?? Page<Product>.DefaultLimit;

        if (pageSize < 1 || pageSize > Page<Product>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Product>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<Product> fetched =
            await products.ListAfterAsync(null, status, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Product> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Product>(items, next);
    }
}
