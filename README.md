# SpaceCraft

Turn one OpenAPI document into SDKs, published automatically to every registry that matters.

Upload a spec, and SpaceCraft validates it, generates a client, compiles it, and publishes
the package — with the version tracking `info.version` so the SDK never drifts from the
contract it was generated from.

---

## Status

The **TypeScript → npm** path is wired end to end and verified: spec upload → validation →
Kiota generation → `npm install`/`tsc`/`npm pack` → `npm publish`. The published package
installs and type-checks in a consumer project under `strict`.

| Capability | State |
|---|---|
| Spec upload, validation, versioning | Working |
| TypeScript SDK generation (Kiota) | Working |
| npm build + publish, idempotent | Working |
| Pipeline runs, steps, logs, artifacts | Working |
| Organizations, members and roles | Working |
| Published SDK history | Working |
| Spec validation findings | Working |
| Account management: profile, password, sessions, deletion | Working |
| Password reset by email | Working |
| Waitlist with double opt-in | Working |
| C#, Java, Python, PHP, Go | Modelled; rejected at the API until wired |
| NuGet, Maven, PyPI, Packagist | Modelled; no publisher yet |
| GitHub Auto-Sync | Modelled on the project; webhook receiver not built |

## Architecture

```
┌──────────────┐        ┌──────────────┐        ┌──────────────┐
│  web (React) │──────▶│  Api         │◀──────│  Worker      │
│  Vite + TSQ  │  REST  │ Minimal API  │  SQL   │ pipeline     │
└──────────────┘        └──────┬───────┘        └──────┬───────┘
                               │                        │
                        ┌──────▼───────┐         ┌──────▼────────┐
                        │  Postgres    │         │ Docker sandbox│
                        │  + job queue │         │ kiota / node  │
                        └──────────────┘         └───────────────┘
                               │                        │
                        ┌──────▼────────────────────────▼───────┐
                        │  Object Storage (S3)                  │
                        │  orgs/{org}/projects/{project}/…      │
                        │  Scaleway deployed · MinIO locally    │
                        └───────────────────────────────────────┘
```

| Project | Responsibility |
|---|---|
| `SpaceCraft.Domain` | Entities and rules. No dependencies. |
| `SpaceCraft.Infrastructure` | EF Core, Postgres, object storage, encryption, spec validation. |
| `SpaceCraft.Pipeline` | Generation, build and publish, plus the sandbox they run in. |
| `SpaceCraft.Api` | Minimal API, organised as vertical slices per feature. |
| `SpaceCraft.Worker` | Long-lived consumer: polls the run queue and executes pipelines. |
| `SpaceCraft.Runner` | Same work as a one-shot process, for schedulers that treat the process as the unit of work (serverless job, batch task, single-use pod). |
| `src/web` | React 19, Tailwind v4, shadcn/ui, TanStack Query, Zustand. |

### Decisions worth knowing

**The queue is a Postgres table.** Workers claim runs with `FOR UPDATE SKIP LOCKED` and hold
a 15-minute lease. A worker that dies mid-build has its run requeued by the lease reaper
rather than leaving it stuck in `Running`. That is one fewer piece of infrastructure to
operate than a broker, and the `IPipelineRunQueue` seam is narrow enough to swap later.

**Generation is delegated to [Kiota](https://github.com/microsoft/kiota).** It already covers
every language on the roadmap and is maintained by Microsoft. The product's value is
orchestration, versioning and publication — not the code emitter. `ISdkGenerator` exists so
that choice can be revisited per language without touching the pipeline.

**The Kiota image is pinned, never `:latest`.** The generator and its per-language runtime
packages are a matched pair. MCR's `:latest` currently resolves to 1.29.1, which emits
`getCollectionOfPrimitiveValues<string>()` while recommending abstractions `preview.103` —
where that method takes an argument. Every SDK containing a primitive array then fails to
compile. `PipelineOptions.Validate` refuses a floating tag outright.

To upgrade Kiota: bump `Pipeline:KiotaImage`, then run one real spec through the pipeline and
confirm the build step passes. `kiota info` reporting a version is *not* evidence that the
generated code compiles against it.

**SDK versions are normalised to semver.** Registries require `major.minor.patch`, and real
specs often do not have one — Keycloak's Admin API declares `"1.0"`. That is normalised to
`1.0.0` during validation, so an unpublishable version fails in the first second rather than
after the build. Calendar versions (`2024-01-15`) are rejected instead of being coerced into
a prerelease that `npm install` would skip by default.

**Execution comes in two shapes, one implementation.** `SpaceCraft.Worker` polls forever and
suits a VM with a Docker daemon. `SpaceCraft.Runner` claims work, does it, and exits — the
shape a serverless job or a cron task needs. Both call the same `IPipelineExecutor`, so there
is one pipeline, not two.

```bash
SpaceCraft.Runner --run <guid>   # execute one run; what a per-run trigger invokes
SpaceCraft.Runner --drain        # work the queue until empty or the budget is spent
SpaceCraft.Runner --reap         # requeue runs abandoned by a dead executor; what cron invokes
```

A run that fails is recorded as failed and the process still exits `0`. Exiting non-zero
would make a scheduler retry the whole invocation and redo work already accounted for; only
the runner being unable to operate earns a non-zero code.

**Isolation is a declared mode, not a boolean.** `Pipeline:Sandbox` is `Container` (nested
Docker containers, needs a daemon), `PlatformIsolated` (the host is itself disposable — a
serverless job, a single-use pod — so steps run in-process and the toolchains must be in the
image), or `None` (no boundary; refused outside Development). The old boolean forced a
deployment into a serverless job to look identical to switching safety off on a laptop.

**Names are restricted so the URL is predictable.** Organizations and projects accept
letters, numbers, spaces and hyphens; the slug is derived from the name and is what appears
in the address. Allowing anything else means a name silently transliterates — "Café/Ürün 2.0"
becoming `cafe-urun-2-0` — and the address stops being something the user chose. Renaming
moves the slug with the name, so the two never drift apart; the settings page shows the new
address before saving and says which links will break.

**Build and publish are separated by design.** The build step runs with *no* registry
credentials mounted and no network beyond the package registry, and `npm install` runs with
`--ignore-scripts`. A malicious lifecycle script in a transitive dependency therefore has
nothing to steal. Only the publish step decrypts a token, for the duration of one command.

**Every step runs in a throwaway container** with no capabilities, no privilege escalation,
a read-only root filesystem, a non-root user, and bounded CPU/memory/PIDs. Generated code is
untrusted input.

**External `$ref`s are rejected.** A spec containing `$ref: http://169.254.169.254/…` would
otherwise make the worker fetch cloud instance metadata on the uploader's behalf.

**The API's own OpenAPI document is the front end's source of types.** `npm run gen:api`
regenerates `src/api/schema.d.ts` from the running API, so a contract change breaks the
build rather than the browser. A product that sells OpenAPI tooling should eat its own.

## Getting started

Requires .NET 10 SDK, Node 20+, and Docker.

```bash
docker compose up -d
```

Postgres, [MinIO](https://min.io) and a local [Verdaccio](https://verdaccio.org) npm registry
come up, and a one-shot container creates the bucket. Verdaccio matters: it lets the publish
step be exercised end to end without pushing anything irreversible to npmjs.org.

Object storage is local, and needs no account. Staging and production point at Scaleway
instead — the application cannot tell the difference, since it speaks the S3 API to whatever
`ObjectStorage:ServiceUrl` names. One setting differs and is already set for you:
`ForcePathStyle`, because MinIO serves bucket-in-path where Scaleway and AWS serve
virtual-hosted style.

```bash
dotnet ef database update --project src/SpaceCraft.Infrastructure --startup-project src/SpaceCraft.Api
```

Then, in three terminals:

```bash
dotnet run --project src/SpaceCraft.Api
```

```bash
dotnet run --project src/SpaceCraft.Worker
```

```bash
npm run dev --prefix src/web
```

- App — http://localhost:5173
- API reference (Scalar) — http://localhost:8080/scalar
- MinIO console — http://localhost:9001 (`spacecraft` / `spacecraft-dev-secret`)
- Verdaccio — http://localhost:4873

The API and worker run on the host rather than in containers: the worker launches Docker
containers of its own for every pipeline step, and containerising it would mean
docker-in-docker or mounting the host socket, which is a lot of privilege for no benefit.

## Secrets in development

`appsettings.json` carries structure and empty placeholders; `appsettings.Development.json`
carries local-only values that are safe in the repository. Anything real — a Scaleway API
key, a production connection string — goes in user secrets, which live outside the working
tree:

```bash
dotnet user-secrets set "Email:Smtp:Password" "<secret key>" --project src/SpaceCraft.Api
```

A `pre-commit` hook refuses any commit that puts a non-empty `ApiKey`, `SecretKey`,
`SigningKey` or `Password` into a tracked settings file. Enable it once per clone:

```bash
git config core.hooksPath .githooks
```

## Deploying

See [docs/deployment.md](docs/deployment.md). In short: the API is a Serverless Container, and
the runner, reaper and migration step are Serverless Jobs sharing one image — Scaleway's
Functions runtimes do not include .NET, and jobs allow far longer executions anyway.

## Tests

```bash
dotnet run --project tests/SpaceCraft.UnitTests
```

```bash
npm run typecheck --prefix src/web
```

## Before this goes to production

These are known and deliberate, not oversights:

- **Data Protection keys must be persisted and KMS-wrapped.** The dev key ring is on disk and
  per-machine; losing it makes every stored registry token unreadable, and copying it makes
  them all readable. See `AddDataProtection` in both hosts.
- **Refresh tokens live in `localStorage`.** An httpOnly `SameSite=Strict` cookie with the
  access token kept in memory is stronger; it needs CSRF handling on the API side.
- **`Jwt:SigningKey` must come from a secret store.** The development value is a placeholder
  and the API refuses to start without one configured.
- **Migrations run at startup only in Development.** In production this belongs in a
  deployment step — concurrent instances racing on one migration corrupts schemas.
- **Maven Central is not a drop-in.** Publishing on a customer's behalf needs a verified
  namespace and GPG signing; it is materially harder than npm or NuGet and should be scoped
  separately.
- **npm needs an automation token.** Classic tokens on a 2FA account prompt for an OTP that
  no pipeline can answer. The API rejects tokens that do not look like automation tokens.
- **The Data Protection key ring must be shared with the runner.** The runner decrypts
  registry tokens, so it resolves the same key ring as the API. In a serverless job that
  means a persisted, KMS-wrapped store both can reach — a container-local key ring makes
  every stored token unreadable to it.
- **The mail sending domain must be verified with Scaleway** (SPF, DKIM, MX) before anything
  is delivered, and `Email:Smtp:Host`/`Username`/`Password` must be set — the API refuses to start
  without them outside Development.
