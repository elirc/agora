namespace Agora.Application.Errors;

/// <summary>Authenticated, but not allowed — the Api maps this to 403.</summary>
// WHY: 403 is reserved for "I know who you are, and you still may not do this to this thing"
// (e.g. a Customer hitting a StoreAdmin action). It is NOT used to hide a resource's existence
// across an ownership boundary — a customer asking for another customer's order gets 404, not
// 403, because confirming the row exists is itself a leak. That ownership rule lands with orders
// (S07); the 403 vocabulary is established here so it is ready when the first role-gated endpoint
// arrives in S03.
public sealed class ForbiddenException(string message = "You do not have permission to do that.")
    : Exception(message);
