# SpaceCraft

Turn one OpenAPI document into SDKs, published automatically to every registry that matters.

Upload a spec, and SpaceCraft validates it, generates a client, compiles it, and publishes
the package — versioned from `info.version`, so the SDK never drifts from the contract it
was generated from.

## Status

| Capability | State |
|---|---|
| Spec upload, validation, versioning | Working |
| TypeScript SDK generation (Kiota) | Working |
| npm build + publish, idempotent | Working |
| Pipeline runs, steps, logs, artifacts | Working |
| Organizations, members and roles | Working |
| Published SDK history | Working |
| Notifications, in-app and by email | Working |
| Account: profile, password, sessions, deletion | Working |
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
| `SpaceCraft.Infrastructure` | EF Core, Postgres, object storage, encryption, spec validation — what the API and the runner share. |
| `SpaceCraft.Pipeline` | Generation (Kiota, in-process), build and publish, and the command runner that starts them on the host. |
| `SpaceCraft.Api` | Minimal API, vertical slices per feature — layout in [CLAUDE.md](CLAUDE.md). |
| `SpaceCraft.Runner` | One run per process: `SpaceCraft.Runner --run <id>`. |
| `src/web` | React 19, Vite, Tailwind v4, shadcn/ui, TanStack Query, Zustand. |

Good to know:

- The run queue is the `pipeline_runs` table (`FOR UPDATE SKIP LOCKED`). The API's
  `RunDispatcher` sweeps it and starts one Serverless Job per run; a run whose lease lapsed
  is re-dispatched by the same sweep.
- Kiota is a package, not a program on the path. To upgrade it, bump `KiotaVersion` in
  `Directory.Packages.props` — it pins both the generator and the runtime versions written
  into generated `package.json` — then run the `@slow` e2e tests: a generation that succeeds
  is not evidence the generated code compiles.
- How untrusted specs and generated code are contained is in [docs/security.md](docs/security.md).
- `src/web/src/api/schema.d.ts` is generated from the running API's OpenAPI document
  (`npm run gen:api`), so a contract change breaks the build rather than the browser.

## Getting started

Requires the .NET 11 SDK (preview until 10 November), Node 22 (`.nvmrc` — the version the
runner image ships and generated SDKs are built against) and Docker.

```bash
docker compose -f compose.development.yaml up -d --build
```

Postgres, [MinIO](https://min.io) (with its bucket), [Mailpit](https://mailpit.axllent.org),
a local [Verdaccio](https://verdaccio.org) npm registry, a fake Scaleway Key Manager and a
fake Serverless Jobs API come up. The fake jobs API starts one runner container per run,
from the image compose just built — hence `--build`, every time the pipeline or the runner
changes. Mail lands in Mailpit; Verdaccio lets the publish step run end to end without
pushing anything irreversible to npmjs.org.

Then, in two terminals — the API migrates the database on startup in Development:

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

### Secrets in development

`appsettings.json` carries structure and the required keys of each section; `appsettings.Development.json`
carries local-only values that are safe to commit. Anything real goes in user secrets:

```bash
dotnet user-secrets set "Smtp:Password" "<value>" --project src/SpaceCraft.Api
```

Enable the pre-commit hook once per clone. It refuses a commit that puts a value shaped like
a real credential into a tracked, non-Development `appsettings*.json`; the hook itself says
what it matches and what it skips.

```bash
git config core.hooksPath .githooks
```

## Tests

```bash
dotnet run --project tests/SpaceCraft.UnitTests
dotnet run --project tests/SpaceCraft.IntegrationTests   # needs Docker
npm run typecheck --prefix src/web
npm test --prefix src/web
```

End to end, against a stack of its own on ports the development stack does not use:

```bash
docker compose -f compose.e2e.yaml up -d --build --wait
npm run test:e2e --prefix src/web
docker compose -f compose.e2e.yaml down
```

`--build`, every time: the API and the runner run as images, and `up` alone reuses the ones
built before your last change. Three tests build a real SDK and are tagged `@slow`; the
first campaign against an empty Verdaccio spends minutes resolving packages, later ones
seconds — `down -v` clears that cache. Skip them with `-- --grep-invert @slow`.

## Deploying

See [docs/deployment.md](docs/deployment.md). Outside Development, startup refuses to run
without key protection, a 32-character signing key, an SMTP relay and an https jobs
endpoint; the required keys are the `"//"` line of each section in both `appsettings.json`
files. Known gaps are listed in [docs/security.md](docs/security.md#known-gaps).
