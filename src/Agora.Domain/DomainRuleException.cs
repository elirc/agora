namespace Agora.Domain;

/// <summary>
/// Thrown when a business invariant would be violated. The Api layer maps this
/// to an RFC 7807 response; the Domain neither knows nor cares about HTTP.
/// </summary>
// LEARN: the Domain speaks in business failures, not status codes. One exception
// type is enough for now — we split it only when a caller needs to react
// differently, never speculatively.
public sealed class DomainRuleException(string message) : Exception(message);
