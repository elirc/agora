namespace Agora.Api.Errors;

/// <summary>Stable problem `type` URIs — part of the API contract (ADR 0002).</summary>
// LEARN: RFC 7807's `type` is the machine-readable half of an error. Clients switch on these
// URIs, not on message text — which means messages can improve freely while integrations keep
// working. Stability here is a compatibility promise.
public static class AgoraProblems
{
    private const string Base = "https://agora.example/problems/";

    public const string NotFound = Base + "not-found";
    public const string InvalidRequest = Base + "invalid-request";
    public const string DomainRuleViolated = Base + "domain-rule-violated";

    // S02: identity. 401 = we don't know who you are; 403 = we do, and you still may not.
    public const string Unauthenticated = Base + "unauthenticated";
    public const string Forbidden = Base + "forbidden";
}
