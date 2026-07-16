using Agora.Application.Events;

namespace Agora.Application.Abstractions;

/// <summary>
/// Where facts about what happened go. Use cases publish; who listens, and what they do about it, is
/// none of a use case's business.
/// </summary>
// ⚠️ A SINK, NOT A BUS — the name is the promise, and it is a small one on purpose.
//
// A bus routes: it knows subscribers, it can be asked whether anybody handled a message, it invites
// `await bus.Send(...)` and then a handler that does the actual work, and one afternoon later half
// the application's control flow is in the middle of a message router where nobody can follow it. A
// SINK takes a fact and returns. It cannot answer a question, and there is no reply — which is
// exactly what makes it safe to publish from inside a use case: the caller is not waiting on
// anything, so an event cannot become a sneaky function call.
//
// So the rule this port exists to enforce: IF YOU NEED SOMETHING TO HAPPEN, CALL IT. Do not publish
// an event and hope. Events are for parties whose existence the publisher should not have to know
// about — a webhook subscriber, an analytics pipeline, an abandoned-cart mailer — and the test is
// simple: if deleting every subscriber breaks the feature, it was never an event, it was a method
// call wearing a costume.
//
// 🔗 today the only adapter writes to the log (Infrastructure/Events/LoggingEventSink), which makes
// this whole seam close to free. That is the point of introducing it NOW rather than in S10: S10
// replaces the ADAPTER with a transactional outbox and a dispatcher, and if this shape is right, not
// one line in Agora.Application changes when it does. A port whose first implementation is a
// one-liner is a port you can still get right. Delivery — who receives an event, how many times, and
// what a redelivery means — is S10's subject and a bigger one than it looks.
public interface IEventSink
{
    Task PublishAsync(AgoraEvent domainEvent, CancellationToken cancellationToken);
}
