using Agora.Application.Abstractions;
using Agora.Application.Events;
using Agora.Domain.Carts;

namespace Agora.Application.Tests.Fakes;

/// <summary>
/// In-memory carts. Mirrors the EF adapter's one load-bearing behaviour: retired carts are invisible
/// to both lookups.
/// </summary>
// LEARN: the filter is copied here ON PURPOSE, not because a fake needs to be thorough for its own
// sake. "A retired cart is unfindable" is where the merge's idempotence actually comes from — if this
// fake returned retired carts, MergeCarts_is_idempotent would pass against the real adapter and fail
// here (or, far worse, the reverse), and the test would be measuring the fake. A fake must lie about
// HOW (a list, not SQL), never about WHAT.
public sealed class InMemoryCartRepository : ICartRepository
{
    public List<Cart> Rows { get; } = [];

    public Task<Cart?> FindActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.SingleOrDefault(cart => !cart.IsRetired && cart.UserId == userId));

    public Task<Cart?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.SingleOrDefault(cart => !cart.IsRetired && cart.TokenHash == tokenHash));

    public void Add(Cart cart) => Rows.Add(cart);
}

/// <summary>An IEventSink that keeps what it was handed, so a test can assert on the facts published.</summary>
public sealed class RecordingEventSink : IEventSink
{
    public List<AgoraEvent> Published { get; } = [];

    public Task PublishAsync(AgoraEvent domainEvent, CancellationToken cancellationToken)
    {
        Published.Add(domainEvent);
        return Task.CompletedTask;
    }

    /// <summary>The one event with this name, or a failure that says what WAS published.</summary>
    public AgoraEvent Single(string name) =>
        Published.SingleOrDefault(published => published.Name == name)
        ?? throw new InvalidOperationException(
            $"No '{name}' event was published. Published: [{string.Join(", ", Published.Select(e => e.Name))}].");
}
