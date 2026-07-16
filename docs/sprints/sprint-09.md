# Sprint 09 — Payments: PayFlow Behind an Anti-Corruption Layer

**Goal:** money leaves the building (fictionally), through an anti-corruption layer, against a vendor
that misbehaves on purpose.

## Scope

**In:** **PayFlow** — an in-repo mock PSP with an awkward dialect (cents-as-integers, statuses like
`"OK_HOLD"`, intermittent 503s) behind an `IPaymentGateway` port; PaymentIntent (create → authorize
→ capture) tied to an order; **anti-corruption layer** translating PayFlow's dialect into a **closed
result union** (`Authorized` / `Captured` / `Declined` / `VendorUnavailable`); order → `paid` on
capture; vendor-fault handling (503 → `VendorUnavailable`, surfaced as a retryable `problem+json`);
payment form UI (card fields → intent → authorize/capture states, error + retry); `docs/http/payments.http`.

**Out:** real card networks; 3-D Secure; saved payment methods; webhooks from PayFlow (S10 gives us
the outbox first); refund execution (S12/S15).

## Teaching focus

The anti-corruption layer: vendor types stop at the adapter; the domain never says "PayFlow." Closed
result unions the application can handle exhaustively vs stringly-typed vendor codes.

## Commit narrative (sketch)

1. `feat(domain): PaymentIntent + closed PaymentResult union`
2. `feat(application): authorize/capture use cases over IPaymentGateway`
3. `feat(vendor): PayFlow mock — the PSP we deserve` (awkward on purpose; dialect documented)
4. `feat(infrastructure): PayFlowGateway adapter — the ACL` (+ adversarial tests: 503s, `"OK_HOLD"`, unknown states)
5. `feat(api): payment endpoints, order→paid on capture, 503 as retryable problem+json`
6. `feat(web): payment form with intent/authorize/capture states + vendor-unavailable retry UX`
7. `docs: ADR-0010 anti-corruption layers, concepts/error-taxonomies.md`

## Teaching placements

- 📘 on the ACL adapter: ubiquitous-language defense — translation lives and dies at the adapter.
- 📘 on the closed union: why `Declined` as a case beats `catch (Exception)` + string matching.
- 🔍 on the adapter tests: "test the adapter against the vendor's worst day, not its brochure."
- ⚠️ on unknown vendor states: the ACL's policy is fail-loud, never guess — a payment you can't
  classify is not a success.
- 🔗 on the `web/` payment form: `VendorUnavailable` is retryable and the UI must say so *without
  inviting a double submission* — the retry button is a correctness question, not a style one.

## Debate

`[Reviewer]`: "The ACL is 200 lines to wrap four calls — call PayFlow from the use case and translate
errors inline." `[Author]`: inline translation is how vendor dialects metastasize; S12 swaps in a
second provider and the checkout use case must not notice. Price of the ACL: one file. Price of no
ACL: every use case becomes a vendor contract. Reviewer concedes, demanding the adapter tests stand
as PayFlow's executable dialect spec — done in commit 4.

## Exercises

- PayFlow returns a brand-new status `"OK_HOLD_2"`. Walk the failure path. Which single file changes?
- Force a 503 mid-capture. What does the customer see, and what state is the order left in?
- The customer's request times out and they click Pay again. Write the test for what the gateway
  sees today. Keep it — you'll want it next sprint.
