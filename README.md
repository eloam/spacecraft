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
│  web (React) │───────▶│  Api         │───────▶│  Runner      │
│  Vite + TSQ  │  REST  │ Minimal API  │ starts │ one job =    │
└──────────────┘        └──────┬───────┘  a job │ one run      │
                               │                └──────┬───────┘
                        ┌──────▼───────┐         ┌──────▼────────┐
                        │  Postgres    │         │ node + npm    │
                        │  + run queue │         │ in the image  │
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
| `SpaceCraft.Pipeline` | Generation, build and publish, and the command runner that starts them on the host. |
| `SpaceCraft.Api` | Minimal API, organised as vertical slices per feature: `Features/<Name>/` holds one file per use case, its `Requests/` and `Responses/` records, and each request's rules in a nested `Validator` (FluentValidation) run by a filter on the API group. |
| `SpaceCraft.Runner` | Same work as a one-shot process, for schedulers that treat the process as the unit of work (serverless job, batch task, single-use pod). |
| `src/web` | React 19, Tailwind v4, shadcn/ui, TanStack Query, Zustand. |

### Decisions worth knowing

**The queue is a Postgres table, and the API starts a job for what is in it.** Nothing else
reads the queue, so a sweep in the API claims queued runs and asks Scaleway to start one job
execution per run — never from the request handler, where a failed start would leave a run
queued and never started. One run per execution is what makes the container boundary mean
something: a build carries one tenant's work, not whatever else was waiting.

The runner claims runs with `FOR UPDATE SKIP LOCKED` and
holds a 25-minute lease, renewed before each step. It only has to outlast the longest single
step, and for a while it did not: the build is allowed fifteen minutes and the lease was
fifteen minutes, so a build that used its whole budget was declared abandoned while still
running and handed to a second worker. `PipelineTimeoutTests` now requires the lease to
outlast the longest step by at least five minutes, so raising a step's budget past that fails
the build rather than reintroducing the race. A runner
that dies mid-build leaves its run `Running` with a lapsed lease, and the same sweep that
starts jobs treats that as a queued run — so recovery is a predicate rather than a second
mechanism somebody has to deploy. That is one fewer piece of infrastructure to operate than a broker, and the
`IPipelineRunQueue` seam is narrow enough to swap later.

**Generation is delegated to [Kiota](https://github.com/microsoft/kiota).** It already covers
every language on the roadmap and is maintained by Microsoft. The product's value is
orchestration, versioning and publication — not the code emitter. `ISdkGenerator` exists so
that choice can be revisited per language without touching the pipeline.

**The generator is a dependency, not a program on the path.** Kiota and its per-language
runtime packages are a matched pair: 1.29.1 emits `getCollectionOfPrimitiveValues<string>()`
while recommending abstractions `preview.103`, where that method takes an argument, so every
SDK containing a primitive array fails to compile. A generator resolved by name off `PATH`
makes that mismatch a property of whichever machine ran, discovered by the customer.

So `Microsoft.OpenApi.Kiota.Builder` is a `PackageReference`, restored like anything else and
identical on a laptop and in the image by construction. The runtime versions written into a
generated `package.json` are not in that library — they are in the CLI package's own
settings, which `SpaceCraft.Pipeline.csproj` embeds at build time via `PackageDownload`, at
the same `KiotaVersion`. Generator and declared dependencies cannot drift apart.

To upgrade Kiota: bump `KiotaVersion` in `Directory.Packages.props` — it moves both — then run
one real spec through the pipeline and confirm the build step passes. A generation that
succeeds is *not* evidence that the generated code compiles.

`npm` is still resolved by name, because `npm install`, `tsc` and `npm publish` have no
in-process equivalent. `.nvmrc` states the version for a host run and
`scripts/check-toolchain.sh` compares it against the Dockerfile and against `PATH`.

**A version is rendered for the registry it is going to, not normalised to semver.**
Registries do not agree, and applying one's rules to the others is wrong in both directions:
`1.2.3.4` is an ordinary NuGet version and a legal Maven one that npm cannot express, and
`1.0` — which is what Keycloak's Admin API declares — must be completed for npm and must not
be for Maven, where `1.0` and `1.0.0` name different artifacts. So `RegistryVersion.Render`
keys on the registry: npm and Packagist complete to three components, NuGet takes up to four
and drops build metadata nuget.org would drop anyway, Maven passes the string through
verbatim, PyPI follows PEP 440 and refuses a local version identifier outright. One rule
governs every arm — complete, never truncate — and where a value cannot be rendered without
losing something it is refused, with a message saying what to write instead. Calendar
versions (`2024-01-15`) are refused everywhere, rather than coerced into a prerelease that
`npm install` would skip by default.

A run renders once, in its validate step, against the registry recorded on it when it was
queued — so a version that registry will not take fails before the generator runs rather than
after the build. Upload-time validation normalises nothing: it renders the declared version
against every registry and reports what it finds. A version no registry can take is an error;
one that some accept and others refuse is a warning naming both lists; `1.0`, which every
registry accepts, is only an Information diagnostic saying it will publish as `1.0.0` to npm
and Packagist and as declared elsewhere. None of those three stops an upload.

```bash
SpaceCraft.Runner --run <guid>   # execute one run; the only thing it does
```

A run that fails is recorded as failed and the process still exits `0`. Exiting non-zero
would make a scheduler retry the whole invocation and redo work already accounted for; only
the runner being unable to operate earns a non-zero code.

**Running unsandboxed is a setting somebody has to write down.** `Pipeline:AllowUnsandboxedExecution`
is a boolean, and outside Development `AddSpaceCraftPipeline` throws unless it is true. It
grants nothing: there is no second runner to select instead, so the flag buys no safety and
changes no behaviour. All it does is make the trade a decision that was taken rather than a
default nobody read.

One tenant per process is no longer part of that trade. The runner takes a single run id and
has no mode that claims a second, so what used to depend on an operator keeping
`MaxRunsPerInvocation` at 1 now holds because there is nothing else the process can do.

**Names are restricted so the URL is predictable.** Organizations and projects accept
letters, numbers, spaces and hyphens; the slug is derived from the name and is what appears
in the address. Allowing anything else means a name silently transliterates — "Café/Ürün 2.0"
becoming `cafe-urun-2-0` — and the address stops being something the user chose. Renaming
moves the slug with the name, so the two never drift apart; the settings page shows the new
address before saving and says which links will break.

**Build and publish are separated by design.** The build step runs with *no* registry
credentials: `NpmSdkBuilder` hands every command an environment of five npm settings and
nothing else, and `LocalCommandRunner` empties the child's environment before filling it, so
the platform's own secrets — the Key Manager token that unwraps the key ring, the storage
keys, the connection string — are not there to inherit. `npm install` runs with
`--ignore-scripts`, so a transitive dependency's lifecycle hook does not execute at all. Only
the publish step decrypts a token, for the duration of one command; it reaches npm as an
environment variable that a generated `.npmrc` refers to by name, so the literal value is in
neither the command line nor a file left behind, and it is registered for redaction so it
cannot survive into a stored step log.

What is *not* there is a network boundary. `NetworkPolicy` is declared on every command the
pipeline runs — `None` for compile and pack, `Registry` for install and publish — and
`LocalCommandRunner` never reads it: steps share the job's network, so a build reaches
whatever the job reaches. The enum is kept because it records intent for the day steps get a
container each. Generation is the one step this does not apply to, and only because it
stopped being a command: it runs in this process, so its refusal to reach the network is a
handler that throws on every request Kiota makes — enforced rather than declared. For the
steps that are still commands, `--ignore-scripts` and the emptied environment are the whole
of the defence.

**Nothing isolates a step from the host.** There is one `ICommandRunner`, `LocalCommandRunner`,
registered unconditionally, and it starts a child process on the machine the runner is on:
no per-step container, no read-only root filesystem, no capability dropping, no CPU, memory
or PID limits. Generated code is untrusted input and it runs unconfined, as the worker's own
user. What stands in for a sandbox is the deployment shape — one Serverless Job execution per
container, torn down afterwards — which holds only while a job carries a single run, and the
runner image starting as uid 10001 rather than root, so a step cannot rewrite the parts of
the image that belong to root. That is a smaller claim than a sandbox, and it is the one the
code supports.

**External `$ref`s are never fetched.** The reader is built with `LoadExternalRefs = false`,
so a spec containing `$ref: http://169.254.169.254/…` cannot make the runner read cloud
instance metadata on the uploader's behalf. It is not followed, which is a different thing
from the document being refused: what is guaranteed here is that nothing is retrieved.

**The API's own OpenAPI document is the front end's source of types.** `src/api/schema.d.ts`
is generated from the document the running API serves, so a contract change breaks the build
rather than the browser. A product that sells OpenAPI tooling should eat its own.

The `gen:api` script that regenerates it does not work on a clean checkout: it invokes
`openapi-typescript`, which is in neither `dependencies` nor `devDependencies` of `src/web`,
so npm finds nothing to run. Install it first, or invoke it through `npx` and let it fetch
the package:

```bash
npx openapi-typescript http://localhost:8080/openapi/v1.json -o src/web/src/api/schema.d.ts
```

## Getting started

Requires .NET 11 SDK (preview until 10 November), Node 22 (`.nvmrc` — it is the version the runner image ships, and the
one the generated SDKs are built against), and Docker.

```bash
docker compose -f compose.development.yaml up -d
```

Postgres, [MinIO](https://min.io), [Mailpit](https://mailpit.axllent.org) and a local
[Verdaccio](https://verdaccio.org) npm registry come up, and a one-shot container creates the
bucket. Mail is delivered to Mailpit and read at http://localhost:8025 — the real transport
runs, MIME and SMTP conversation included, and nothing leaves the machine. It has no
certificate, which is why `Smtp:Security` is `None` locally and refused anywhere else. Verdaccio matters: it lets the publish
step be exercised end to end without pushing anything irreversible to npmjs.org.

If you set `ObjectStorage:AccessKey` and `SecretKey` in user secrets when storage was on
Scaleway, **remove them** — user secrets are read after `appsettings.Development.json`, so the
old Scaleway keys win and MinIO refuses a key it has never heard of:

```bash
dotnet user-secrets remove "ObjectStorage:AccessKey" --project src/SpaceCraft.Api
dotnet user-secrets remove "ObjectStorage:SecretKey" --project src/SpaceCraft.Api
```

Object storage is local, and needs no account. Staging and production point at Scaleway
instead — the application cannot tell the difference, since it speaks the S3 API to whatever
`ObjectStorage:ServiceUrl` names. One setting differs and is already set for you:
`ForcePathStyle`, because MinIO serves bucket-in-path where Scaleway and AWS serve
virtual-hosted style.

```bash
dotnet ef database update --project src/SpaceCraft.Infrastructure --startup-project src/SpaceCraft.Api
```

Then, in two terminals — the runner is already running in compose, from the image production deploys:

```bash
dotnet run --project src/SpaceCraft.Api
```

```bash
npm run dev --prefix src/web
```

- App — http://localhost:5173
- API reference (Scalar) — http://localhost:8080/scalar
- MinIO console — http://localhost:9001 (`spacecraft` / `spacecraft-dev-secret`)
- Mailpit inbox — http://localhost:8025
- Verdaccio — http://localhost:4873

The API runs on the host because that is what you are editing. The runner runs in compose,
from the image the Serverless Job deploys, so the pipeline you exercise locally is the one
that ships.

## Secrets in development

`appsettings.json` carries structure and empty placeholders; `appsettings.Development.json`
carries local-only values that are safe in the repository. Anything real — a Scaleway API
key, a production connection string — goes in user secrets, which live outside the working
tree:

```bash
dotnet user-secrets set "Smtp:Password" "<secret key>" --project src/SpaceCraft.Api
```

A `pre-commit` hook refuses a commit that puts something *shaped like* a real credential into
a tracked `appsettings*.json` — a UUID, a long random-looking string, or a known vendor prefix
(`npm_`, `ghp_`, `SCW`, `sk-`, `xox`) under `ApiKey`, `SecretKey`, `AccessKey`, `SigningKey`,
`Password`, `Token` or `ClientSecret`. It judges the value, not the key, because a rule that
cries wolf on an empty value gets bypassed with `--no-verify` and then protects nothing. Two
things follow from that, and both are worth knowing: every `*Development.json` is skipped
outright, so the hook will not stop you committing a real key into one, and a secret that
looks like a passphrase rather than a random string passes. It is a net, not a boundary.
Enable it once per clone:

```bash
git config core.hooksPath .githooks
```

## Deploying

See [docs/deployment.md](docs/deployment.md). In short: the API is a Serverless Container, and
the runner is a Serverless Job — Scaleway's
Functions runtimes do not include .NET, and jobs allow far longer executions anyway.

## Tests

Three suites, in the order they cost. The unit tests answer in half a second, the integration
tests stand up Postgres and object storage in containers, and the end-to-end tests open a
browser against the whole application — so they cover the paths that cross a boundary and
leave the rest to the two suites that can afford to be thorough.

```bash
dotnet run --project tests/SpaceCraft.UnitTests
dotnet run --project tests/SpaceCraft.IntegrationTests
```

```bash
npm run typecheck --prefix src/web
```

End to end, against a stack of its own — its own database, object storage, mailbox, API and
runner, on ports the development stack does not use, so a campaign can never write into the
database you are working in:

```bash
docker compose -f compose.e2e.yaml up -d --build --wait
npm run test:e2e --prefix src/web
docker compose -f compose.e2e.yaml down
```

`--build`, every time: the stack runs the API and the runner as images, and `up` alone happily
starts the ones it built before your last change to them. The front end has no such trap — the
test run builds and serves it itself.

Three of the twenty-one build a real SDK — one downloads it, one publishes it to the Verdaccio
in the stack and asks the registry whether it arrived, and one revokes the credential first and
checks that nothing did — so the first campaign
against an empty Verdaccio spends minutes resolving packages from npmjs and the ones after it
spend seconds.
`down` keeps that cache; `down -v` clears it along with everything else. To skip that test:

```bash
npm run test:e2e --prefix src/web -- --grep-invert @slow
```

The Playwright config lives in `src/web`, not at the repo root, so `playwright test` has to be
run from there — hence `--prefix`, and the `--` that hands the rest of the line to Playwright
rather than to npm.

## Before this goes to production

These are known and deliberate, not oversights:

- **A real Key Manager key must be configured.** The key ring is not on disk anywhere:
  `AddSpaceCraftDataProtection` calls `PersistKeysToDbContext` unconditionally, so it lives in
  the `spacecraft.data_protection_keys` table beside the registry tokens it encrypts — which
  is also why it has to be wrapped. Development points at the fake Key Manager in
  `infra/SpaceCraft.FakeScalewayKms`; production has to set `KeyProtection:BaseUrl`,
  `Region`, `KeyId` and `SecretKey` against the real service. Outside Development, startup
  throws if the provider is unset, if any of those is missing or malformed, or if the URL is
  not `https` — rather than writing the ring in clear into the database it is meant to protect.
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
  no pipeline can answer, and nothing in the API stops one being stored. The shape check
  applies only when the registry is npm *and* the endpoint's host is `registry.npmjs.org` —
  a self-hosted Verdaccio, Nexus or Artifactory mints its own formats, so holding those to
  npm's would refuse the very credentials the endpoint field exists to store. Even where it
  applies it accepts a bare UUID as well as an `npm_` prefix, and a classic token is a UUID,
  so it cannot tell the two apart. Every other credential is only checked for being at least
  eight characters. The wrong token is discovered by a publish failing.
- **The Data Protection key ring must be shared with the runner.** The runner decrypts
  registry tokens, so it resolves the same key ring as the API. In a serverless job that
  means a persisted, KMS-wrapped store both can reach — a container-local key ring makes
  every stored token unreadable to it.
- **The mail sending domain must be verified with Scaleway** (SPF, DKIM, MX) before anything
  is delivered, and `Smtp:Host`/`Username`/`Password` must be set — the API refuses to start
  without them outside Development.
