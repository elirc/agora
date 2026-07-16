namespace Agora.Application.Errors;

/// <summary>
/// The request itself is malformed (bad cursor, out-of-range limit) — as opposed to a
/// DomainRuleException, where the request was well-formed but the business said no.
/// The Api maps this to 400; domain rule violations map to 422.
/// </summary>
// LEARN: two failure families, two exception types, two status codes. Collapsing them
// into one "BadRequestException" loses exactly the distinction clients script against.
public sealed class RequestValidationException(string message) : Exception(message);
