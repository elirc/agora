using Agora.Application.Abstractions;
using Agora.Application.Events;
using Microsoft.Extensions.Logging;

namespace Agora.Infrastructure.Events;

/// <summary>Writes each fact to the log. Agora's only <see cref="IEventSink"/> today.</summary>
// 🔗 THE ADAPTER S10 REPLACES. This is a deliberately tiny implementation of a deliberately small
// port, and both of those are the point: the reason `IEventSink` exists in S05 rather than in S10 is
// that introducing a port at the same time as its real implementation teaches you nothing about
// whether the port was right. Here, the call sites are written first and the interesting adapter
// arrives later — which is the only order in which "we swapped the adapter and no use case changed"
// is a claim rather than a coincidence. If S10 has to reach back into Agora.Application to make the
// outbox fit, the shape below was wrong, and that will be a finding worth having.
//
// LEARN: structured logging, not string concatenation. `{EventName}` is a FIELD — Serilog, OTel, or
// any structured backend indexes it, so "every cart.merged in the last hour" is a query rather than a
// grep. `$"Domain event {name}"` throws that away and produces a million unique strings that only a
// human can read. The braces are not formatting; they are schema.
internal sealed class LoggingEventSink(ILogger<LoggingEventSink> logger) : IEventSink
{
    public Task PublishAsync(AgoraEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        logger.LogInformation(
            "Domain event {EventName} occurred at {OccurredAtUtc}: {EventData}",
            domainEvent.Name,
            domainEvent.OccurredAtUtc,
            domainEvent.Describe());

        // WHY a completed task and not `async`: there is nothing to await. The port is async because
        // its INTERESTING implementations are (an outbox insert, an HTTP post), and a port whose shape
        // is dictated by its most trivial adapter is a port that has to change the day a real one
        // arrives. Task.CompletedTask allocates nothing and says so honestly.
        return Task.CompletedTask;
    }
}
