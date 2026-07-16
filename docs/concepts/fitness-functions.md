# Fitness Functions: Architecture You Can Fail

**One-pager · Sprint 01 · pairs with ADR 0001 and ADR 0003**

## The idea

An architectural principle that lives in a diagram is a hope. The same principle expressed as a
test is a *fitness function*: an automated check that fails the build when the architecture
degrades. Agora's dependency rule is not documentation — it is `tests/Agora.ArchitectureTests`,
red or green on every PR. And it has a frontend twin: an ESLint rule that keeps `fetch` out of
components.

## Why teams need them

Erosion is never a decision. It's a Tuesday: a deadline, an endpoint that needs one EF query, a
`using Agora.Infrastructure;` that saves twenty minutes. Or, in the browser, a component that calls
`fetch` directly "just this once." Each step is locally reasonable; eighteen months later the
"hexagon" is a ball of mud with project boundaries as decoration, and the React app fetches from
everywhere. A fitness function converts each of those Tuesdays from a silent erosion into a red
build that forces the conversation *now*, when the fix costs minutes.

## What Agora enforces (Sprint 01)

| Rule | Test | Layer |
|---|---|---|
| Domain references no layer, no framework | `Domain_depends_on_no_other_layer_and_no_framework` | backend |
| Application references only Domain | `Application_depends_only_on_Domain` | backend |
| Contracts are a dependency-free leaf | `Contracts_are_a_leaf_with_no_agora_dependencies` | backend |
| Infrastructure never reaches up | `Infrastructure_never_reaches_up_into_Api_or_Contracts` | backend |
| Api touches Infrastructure only in the composition root | `Api_touches_Infrastructure_only_inside_the_composition_root` | backend |
| Every mapped entity property is actually mapped | `ModelMappingTests` | backend |
| The served OpenAPI matches the committed document | `OpenApiDriftTests` | contract |
| Generated FE types match the committed document | `npm run api-types:check` | contract |
| Components never call `fetch` directly | ESLint `no-restricted-globals` (exempting `src/api/`) | frontend |

The last three are one idea wearing three hats: *one contract, two enforced consumers*. The OpenAPI
document is the treaty; the backend drift test and the frontend type-drift check both fail if either
side wanders from it, and the ESLint rule keeps the browser's only door to that contract inside
`src/api/`.

## The senior habit

When you read an unfamiliar codebase, look for its fitness functions first. They tell you which
principles the team actually enforces, as opposed to the ones in the wiki. If there are none, the
architecture is whatever the last six months of deadlines made it.

## Try it

1. Add `using Agora.Infrastructure;` + an `AgoraDbContext` field to `CategoryEndpoints`. Run
   `dotnet test`. Read the failure message — it names the offending type.
2. Add `fetch('/api/v1/products')` inside a component. Run `npm run lint`. Read the rule's message.
3. Harder: find a violation the architecture tests would *miss*. (Hint: reflection, service
   location.) That gap is why fitness functions complement review rather than replace it.
