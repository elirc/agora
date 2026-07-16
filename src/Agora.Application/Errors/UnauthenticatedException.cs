namespace Agora.Application.Errors;

/// <summary>No valid session — the caller is anonymous. The Api maps this to 401.</summary>
// LEARN: 401 means "I don't know who you are" — authentication is missing or stale. It is
// distinct from 403 ("I know who you are; you still may not"). Login failures are also 401, with
// a DELIBERATELY uniform message, so the response can't be used to enumerate which emails exist.
public sealed class UnauthenticatedException(string message = "Authentication required.")
    : Exception(message);
