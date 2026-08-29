# Deploying on Scaleway

Scaleway Serverless **Functions** support Python, Node, Go, PHP and Rust — not .NET. Every
component here is therefore a **Serverless Container** (for anything serving HTTP) or a
**Serverless Job** (for anything that runs and exits). That is not a workaround: containers
and jobs take any OCI image, and jobs allow up to 24 hours per execution, against the 15
minutes a function-style platform typically permits.

## What runs where

| Component | Scaleway product | Image | Invocation |
|---|---|---|---|
| API | Serverless Container | `src/SpaceCraft.Api/Dockerfile` | HTTP, scale-to-zero |
| Pipeline runner | Serverless Job | `src/SpaceCraft.Runner/Dockerfile` | `--run <id>` from the API, or `--drain` |
| Lease reaper | Serverless Job | same image | `--reap`, cron `* * * * *` |
| Migrations | Serverless Job | same image | `--migrate`, once per release |
| Database | Managed Database for PostgreSQL | — | — |
| Specs, artifacts, logs | Object Storage | — | S3-compatible |
| Email | Transactional Email | — | API |

One image serves the runner, the reaper and the migration step; they differ only by argument.

## The API container

The platform injects `PORT` and routes to it. `Program.cs` binds `0.0.0.0` on that port —
binding loopback instead makes the startup probe fail, and with it the deployment.

Scale-to-zero is safe here: the API holds no state between requests, and the queues it writes
to live in Postgres.

## Sizing the runner job

`/tmp` is backed by memory in the container sandbox, and the build step writes there. Measured
on the Keycloak Admin API — 382 KB of spec, 11,000 lines of generated models:

```
178 MiB   peak process (npm install + tsc)
 28 MB    node_modules, in RAM because /tmp is tmpfs
  3 MB    compiled output
```

Allocate **1 GiB** for the runner job. 512 MiB fits the measurement but leaves nothing for a
larger spec, and the failure mode is an OOM kill mid-build rather than a clear error.

`SandboxMode.PlatformIsolated` is set in the runner image: nested containers are unavailable,
so pipeline steps run in-process and the toolchains are baked in. See
[security.md](security.md) for exactly which controls that gives up — the loss of
`--network none` during generation matters most, and it matters much more the day a language
whose build system executes code (Gradle, Maven, `setup.py`) is added.

## Order of operations for a release

1. Build and push both images.
2. Run the migration job to completion — **not** at container startup. Two API containers
   coming up together would race on the same migration.
3. Deploy the API container and the job definitions.

`Database:MigrateOnStartup` stays `false` outside Development, so nothing migrates implicitly.

## Configuration

.NET reads configuration from `appsettings.json`, `appsettings.{Environment}.json`, user
secrets (Development only) and **environment variables** — in that order, each overriding the
last. It does *not* read `.env` files, and neither does anything else here, so the settings
below are supplied as environment variables in the container and job definitions.

Nested keys use a double underscore: `ConnectionStrings__Postgres` maps to
`ConnectionStrings:Postgres`.

```
# Database
ConnectionStrings__Postgres=Host=…;Port=5432;Database=spacecraft;Username=…;Password=…

# Auth — validated at startup, at least 32 bytes
Jwt__SigningKey=…
Jwt__Issuer=spacecraft
Jwt__Audience=spacecraft-api

# Scaleway account — shared by every service that talks to it
Scaleway__ProjectId=…

# Object storage — its own IAM key, distinct from the email one
ObjectStorage__BucketName=spacecraft
ObjectStorage__ServiceUrl=https://s3.fr-par.scw.cloud
ObjectStorage__Region=fr-par
ObjectStorage__AccessKey=…
ObjectStorage__SecretKey=…

# Email — startup fails without these outside Development
# SMTP rather than a provider API: the relay is swappable without touching code, and
# nothing below is derived from one. Scaleway's username is the id of the project the
# sending domain belongs to.
Email__Smtp__Host=smtp.tem.scaleway.com
Email__Smtp__Port=587
Email__Smtp__Username=…
Email__Smtp__Password=…
Email__Smtp__FromEmail=noreply@yourdomain
Email__Smtp__FromName=SpaceCraft

# Pipeline — baked into the runner image, override only to change it
Pipeline__Sandbox=PlatformIsolated
Pipeline__NpmRegistry=https://registry.npmjs.org

# Public URLs and CORS
PublicUrls__Api=https://api.yourdomain
PublicUrls__App=https://app.yourdomain
Cors__AllowedOrigins__0=https://app.yourdomain

# Rate limits (optional; these are the defaults)
RateLimits__GlobalPermitsPerMinute=300
RateLimits__CredentialPermitsPerWindow=10
RateLimits__CredentialWindow=00:15:00
```

Startup fails fast on a missing signing key, missing email credentials, an unpinned generator
image, or `Pipeline:Sandbox=None`, rather than running degraded.

It also refuses to start while any setting still reads `[REQUIRED]` — the marker
`appsettings.json` uses for what a deployment must supply — and names every one of them at
once. Without that check the placeholder is just a string: it satisfies `[Required]`, passes
every option validation, and reappears much later as a rejected SMTP password or a 403 from
S3, with nothing tying either back to a variable nobody set.

This holds in Development too. A checkout with no account behind it blanks these in
`appsettings.Development.json` or user secrets, which is a deliberate act — unlike a marker
nobody noticed.

The relay's certificate is validated in full. Revocation is always requested; what
`Email__Smtp__RequireCertificateRevocation` decides is what happens when no answer comes back,
which on macOS and in a container with no route to an OCSP responder is every time.

It is **on** for a deployment: a host that cannot get an answer about its relay's certificate
should say so rather than shrug. `appsettings.Development.json` turns it off, because .NET on
macOS never completes the check and a valid certificate is rejected — turn it off in a
container too if it has no route to a responder, and know what that buys you.

Nothing else moves either way: an expired certificate, an untrusted root, a mismatched name
and a revocation that comes back positive are refused in both modes.

`Email:Smtp:Port` and `UseImplicitTls` have to agree — 465 and 2465 expect TLS from the first
byte, 25, 587 and 2587 upgrade with STARTTLS. Crossed, neither side reports an error: the
client waits for a greeting the server will not send until it sees TLS, and the send ends as
a timeout several retries later with nothing naming the cause.

Mail is checked the same way from the other side: `Email:Smtp` wants Host, Username and
Password **together, or none of them**. All three blank is the state of a machine with no
relay, and selects the transport that writes to the log. One or two of them is somebody who
started configuring a relay and stopped, and reading that as "no relay" would swallow every
message the application sends.

**In local development** none of this is needed: `appsettings.Development.json` covers it, and
anything genuinely secret goes in user secrets:

```bash
dotnet user-secrets set "Email:Smtp:Password" "<secret key>" --project src/SpaceCraft.Api
```

**Two API keys, one project.** Transactional Email and Object Storage each get their own IAM
application, so a key that can send mail cannot read the bucket and vice versa. Both live in the
Scaleway project named by `Scaleway__ProjectId` — that is the only thing they share, and it is
why the project id is not filed under either service.

**A bucket per environment.** Development and production use separate buckets, created once in
the console; nothing creates them at startup. Scope the development key to the development
bucket, so a wrong `BucketName` there fails instead of writing into production.

Objects are laid out by ownership:

```
orgs/{org}/projects/{project}/specs/{name}-{spec}.yaml
orgs/{org}/projects/{project}/runs/{run}/artifact/{fileName}
orgs/{org}/projects/{project}/runs/{run}/logs/{step}.log
```

One prefix per project is what makes a per-project lifecycle rule or bulk delete possible. After
a change to that layout, `--relocate-storage` on the runner image moves existing objects onto
it; `--dry-run` first, since it deletes the sources once the copies are confirmed.

Two prerequisites are not configuration and cannot be worked around:

- **The sending domain must be verified** in the Scaleway console (SPF, DKIM, MX) or every
  email is rejected.
- **The Data Protection key ring must be persisted and shared** between the API and the
  runner. The API encrypts registry tokens; the runner decrypts them to publish. A key ring
  local to each container means the runner cannot read what the API wrote — and losing it
  makes every stored token permanently unreadable. This is the one piece of the deployment
  still to build.

## Latency to expect

Serverless Jobs are guaranteed to reach *Running* within ten minutes of being triggered.
Typical start is far quicker, but the SLA is the number to design against: the UI must
distinguish "queued" from "running", or a slow start looks like a failure. Anything needing an
immediate answer — spec validation on upload — stays in the API, synchronously, where it
already is.
