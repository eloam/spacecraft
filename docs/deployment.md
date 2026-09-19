# Deploying on Scaleway

Scaleway Functions have no .NET runtime, so the API runs as a Serverless Container and the
pipeline runner as a Serverless Job — any OCI image, executions up to 24 hours.

## What runs where

| Component | Scaleway product | Image | Invocation |
|---|---|---|---|
| API | Serverless Container | `src/SpaceCraft.Api/Dockerfile` | HTTP, minimum one instance |
| Pipeline runner | Serverless Job | `src/SpaceCraft.Runner/Dockerfile` | `--run <id>` (or `SPACECRAFT_RUN_ID`), started by the API, one execution per run |
| Database | Managed Database for PostgreSQL | — | — |
| Specs, artifacts, logs | Object Storage | — | S3-compatible; MinIO in development |
| Email | Transactional Email | — | SMTP relay |

Both images build on the `11.0-preview` .NET tag (`global.json` pins the SDK) until .NET 11
goes GA on 10 November 2026; the tag and the pin have to be revisited then.

Cloud resources — the database and its roles, the buckets, the IAM keys, the Key Manager key,
the API container and the `spacecraft-pipeline` job definition — are created outside this
repository.

## The API container

- The image listens on **8080** (`ASPNETCORE_HTTP_PORTS` from the aspnet base image).
  Declare port 8080, or set `ASPNETCORE_HTTP_PORTS` to whatever you declare. Nothing reads `PORT`.
- **Minimum one instance.** `RunDispatcher` runs inside the API and is the only thing that
  starts jobs; scaled to zero, queued runs wait until somebody loads a page. Several instances
  are safe: the queue is claimed in Postgres.
- Put the gateway's CIDR in `ForwardedHeaders:KnownNetworks`, or every client shares the
  gateway's address in the rate limiter.

## The runner job

- Allocate **1 GiB** of memory: `/tmp` is tmpfs and the build (`npm install` + `tsc`) writes
  there. The failure mode of too little is an OOM kill mid-build.
- One execution per run. The API starts it with `POST …/serverless-jobs/v1alpha2/regions/{region}/job-definitions/{id}/start`
  and `args: ["--run", "<guid>"]`. `PipelineJobs:SecretKey` is the only credential that
  starts compute: give it to the API container, never to the job definition.

## Releasing

`release.yml` runs on a `v*` tag: build, unit tests, `dotnet ef migrations has-pending-model-changes`,
then both images pushed to the registry. What follows is manual, in this order:

1. Apply the migration bundle.
2. Point the container and the job definition at the new image.

Startup migration exists only in Development; outside it the setting is ignored.

### Migrations

Schema changes ship as an EF migration bundle, run with a role that may change the schema —
the API and the runner should not hold one:

```bash
dotnet ef migrations bundle --self-contained --target-runtime linux-x64 \
  --project src/SpaceCraft.Infrastructure --startup-project src/SpaceCraft.Api \
  --output artifacts/efbundle
./artifacts/efbundle --connection "$MIGRATION_CONNECTION"
```

Rolling back is the same executable with a target — `./efbundle <PreviousMigration>` runs
`Down` and can lose data. To review first: `dotnet ef migrations script --idempotent`.

If the bundle ever runs from a GitHub-hosted runner, the database allow-list has to admit
GitHub's ranges; put that job behind an environment with required reviewers.

## Configuration

.NET reads `appsettings.json`, `appsettings.{Environment}.json`, user secrets (Development)
and environment variables, in that order. Nested keys use `__`: `ConnectionStrings__Postgres`.

The required keys are the `"//"` line at the top of each section in
`src/SpaceCraft.Api/appsettings.json` and `src/SpaceCraft.Runner/appsettings.json`. Anything
required and blank is refused at startup with the key named; `KeyProtection` additionally
does one encrypt/decrypt round trip against Key Manager and refuses to start if it fails.

| Section | Read by |
|---|---|
| `ConnectionStrings`, `ObjectStorage`, `KeyProtection` | API and runner — identical `KeyProtection` on both: they share one key ring |
| `Jwt`, `PipelineJobs`, `PublicUrls`, `Smtp`, `RateLimits`, `Notifications`, `ForwardedHeaders` | API |
| `Pipeline` (`AllowUnsandboxedExecution=true` is required) | runner |

- **Secrets as files.** Both hosts read `/run/secrets` last: a file named
  `/run/secrets/ConnectionStrings__Postgres` becomes `ConnectionStrings:Postgres` and wins over
  an environment variable of the same name. Mount the runner's secrets as files, so a
  customer's build cannot read them from `/proc/self/environ`.
- **SMTP.** `Host`, `Username` and `Password` all set or all blank (blank is the logging
  transport, Development only); `Security` is `StartTls`, `ImplicitTls` or `None` (`None`
  refused outside Development); 465/2465 need `ImplicitTls`. Details on `SmtpEmailOptions`.
  Verify the sending domain (SPF, DKIM, MX) with Scaleway before any mail leaves.
- **Object storage.** One bucket per environment, created outside the application; scope
  each environment's key to its bucket. Objects live under `orgs/{org}/projects/{project}/…`
  (`StorageKeys.cs`); nothing migrates stored objects if that layout changes.
