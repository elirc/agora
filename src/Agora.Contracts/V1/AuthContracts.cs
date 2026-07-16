namespace Agora.Contracts.V1;

/// <summary>Body of POST /api/v1/auth/register — self-service shopper sign-up.</summary>
public sealed record RegisterRequest(string? Email, string? Password);

/// <summary>Body of POST /api/v1/auth/login.</summary>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>
/// The authenticated caller, as returned by register, login, and GET /api/v1/auth/me.
/// </summary>
// WHY there is NO token field here: with cookie sessions the token travels in an HttpOnly
// Set-Cookie header, NOT the response body — JavaScript can't read it, which is the whole point
// (an XSS on the page cannot steal a cookie it cannot see). A bearer-token API would return the
// token here; a cookie API deliberately does not. That single absence is the contract-level tell
// that Agora chose cookies. See docs/adr/0004.
public sealed record MeDto(Guid Id, string Email, string Role);
