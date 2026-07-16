# Sprint 10 — Reliability: Outbox, Idempotency, Signed Webhooks (Integration Flagship)

**Goal:** Agora can be integrated with honestly — events that survive a crash, retries that don't
double-charge, webhooks that don't lie. Two defects from earlier chapters fail out loud, then this
sprint's machinery fixes them.

## Scope

**In:** **transactional outbox** (`IEventSink` → outbox adapter; domain events written in the same
transaction as state; background dispatcher delivers with exponential backoff; **S5 call sites
unchanged**); **`Idempotency-Key` middleware**, mandatory on money-moving POSTs (key + request-hash
stored; replay returns the original response; same key + different body → 422); **HMAC-SHA256-signed
order webhooks** (WebhookSubscription: endpoint + secret + event filter; `X-Agora-Signature:
t=...,v1=...`; timestamp tolerance) + delivery log + redelivery endpoint; **two harvest arcs**
(below); admin webhook-subscriptions UI + delivery-log view; `docs/http/webhooks.http`.

**Out:** inbound webhooks; partner API keys (kept minimal / S13); a real queue (the outbox survives
that move — the port is the table).

## The harvest arcs — in-PR commits

**Arc A — a retry double-charges.** RED: the money-moving POST is driven twice (client retry) and
`IPaymentGateway` is observed invoked twice. Fix: the `Idempotency-Key` middleware + a use-case
natural-key guard; replay returns the original result.

**Arc B — events lost or phantom around commit.** RED #1: an event is emitted for a transaction that
never committed (phantom). RED #2: a crash after commit loses the event with no durable record. Fix:
events become outbox rows written *in* the transaction; the dispatcher delivers post-commit; both
REDs go green.

## Commit narrative (sketch)

1. RED: money-moving POST called twice → gateway invoked twice · 2. RED: phantom event + lost event ·
3. `feat(infrastructure): transactional outbox — rows in the tx, dispatcher post-commit` (arc B green) ·
4. `feat(api): Idempotency-Key middleware — key+hash store, replay, 422 on body mismatch` (arc A green) ·
5. `feat(application+infrastructure): webhook subscriptions + HMAC delivery + delivery log + redelivery` ·
6. `feat(web): webhook-subscriptions admin + delivery-log view` ·
7. `docs: ADR-0011 the outbox, ADR-0012 idempotency middleware, concepts/at-least-once.md`

## Teaching placements

- 📘 on the outbox: the two wrong options (publish-after-commit loses; publish-in-tx-to-broker lies)
  and why rows-in-the-transaction is the honest third.
- 📘 on HMAC signatures: constant-time compare, timestamp tolerance, the replay window.
- 🔍 on the dispatcher: "read a background worker for its failure modes first — poison messages,
  ordering, shutdown."
- ⚠️ on idempotency storage: the key alone isn't enough — hash the body; same key + different body
  is a client bug to surface, not mask.
- 🔗 on the `web/` delivery-log view: an integration you can't see is one you can't operate — the
  delivery log is the Ops UI that makes at-least-once observable.

## Debate

`[Reviewer]`: "A hosted-service dispatcher inside the API process is a toy — use a real queue."
`[Author]`: a queue moves the at-least-once problem, it doesn't solve it; the outbox is the part that
matters and it survives a later move to any broker (the port is the outbox table). In-process polling
is honest for one store and testable in CI; ADR-0011 records the migration trigger. Reviewer: "agreed
— and the S15 runbook must cover dispatcher lag."

## Exercises

- Subscribe a webhook, break its endpoint, watch backoff in the delivery log, then redeliver.
- Send the same `Idempotency-Key` with a different body. Which status, and why is masking it the
  wrong mercy?
- Forge a webhook body and try to make the signature verify. Name each defense you hit, in order.
