namespace Agora.Api.Tests.Infrastructure;

/// <summary>The subset of an RFC 7807 problem+json body the tests assert on.</summary>
public sealed record ProblemShape(string Type, string Title, int Status, string Detail);
