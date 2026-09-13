# Deploying on Scaleway

Scaleway Serverless **Functions** support Python, Node, Go, PHP and Rust — not .NET. Every
component here is therefore a **Serverless Container** (for anything serving HTTP) or a
**Serverless Job** (for anything that runs and exits). That is not a workaround: containers
and jobs take any OCI image, and jobs allow up to 24 hours per execution, against the 15
minutes a function-style platform typically permits.

## What runs where

| Component | Scaleway product | Image | Invocation |
|---|---|---|---|
| API | Serverless Container | `src/SpaceCraft.Api/Dockerfile` | HTTP, minimum one instance |
| Pipeline runner | Serverless Job | `src/SpaceCraft.Runner/Dockerfile` | `--run <id>`, started by the API, one execution per run |
| Migrations | Migration bundle | built in CI | applied from the release pipeline, once per release |
| Database | Managed Database for PostgreSQL | — | — |
| Specs, artifacts, logs | Object Storage | — | S3-compatible; MinIO in development |
| Email | Transactional Email | — | SMTP |

The runner image serves one purpose and takes one argument. Recovering a run whose worker died
used to be a second job on a cron; it is now a predicate in the dispatcher's claim, so there is
no schedule to create and a dead executor costs seconds rather than a cron interval.

Mail leaves over SMTP, not over Scaleway's REST API. `SmtpEmailTransport` opens a MailKit
connection to the relay named in `Email:Smtp:Host`; there is no HTTP client, no route and no
token on that path. Scaleway happens to be the relay, and everything the application knows
about it is a hostname, a port and a password.

**Both deployed images are built on a preview tag.** The API and runner Dockerfiles take
`ARG DOTNET_VERSION=11.0-preview`, `Directory.Build.props` targets `net11.0`, and `global.json`
pins the SDK to `11.0.100-preview.7.26381.103`. There is no plain `11.0` tag on mcr until .NET
11 goes GA on 10 November 2026, and the `-preview` tags stop being updated the same day — so an
image built on them after that date is one that no longer receives patches, and the tag and the
pin both have to be revisited then.

## The API container

The platform injects `PORT` and routes to it. `Program.cs` binds `0.0.0.0` on that port —
binding loopback instead makes the startup probe fail, and with it the deployment.

**The API must not scale to zero.** It holds no state between requests and the queues it
writes to live in Postgres, so nothing about a request needs it — but `RunDispatcher` is a
background service inside it, and it is the only thing that starts a pipeline job. Scaled to
zero there is no dispatcher, so a run queued while nobody is browsing is not started until
somebody loads a page. That is the missing-trigger failure again, intermittently, and no test
would report it.

Set a minimum of one instance. The dispatcher is safe to run in several at once — it claims
its batch the same way the runner claims a run — so scaling up costs nothing but the sweep's
own queries.

The mail dispatchers are the second reason, and they are weaker but real: they drain an
in-memory channel, so an instance that goes away takes whatever it was holding with it. A
minimum of one does not make that safe, only rarer — "min 1" is not "the same one", and a
redeploy still replaces the process.

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

Pipeline steps run in the runner's own process. Generation is no longer one of those steps:
Kiota is a `PackageReference` of `SpaceCraft.Pipeline` and the builder is called in process, so
the image carries Node and nothing else, and `npm` — install, pack, publish — is the only name
the pipeline resolves off `PATH`. The isolation is the job's: one execution per container, torn
down afterwards — which holds only while a job runs a single run, so the API starts one
execution per run with `--run <id>`. That is the only thing the runner can do with a queue:
the mode that drained several runs through one process was removed, along with the setting
that was supposed to hold it at one. The image also carries no `CMD`, so a definition created
without an explicit command fails loudly rather than falling back to something.
See [security.md](security.md) for what that gives up, and for why it becomes a real decision
the day a language whose build system executes code (Gradle, Maven, `setup.py`) is added.

### One image per language, not one image with every language

Decided before the second language rather than during it, because the answer shapes the
image, the triggers and the isolation together.

A single image carrying every toolchain is the cheaper thing to build and the wrong thing to
run. A JDK and Maven are around 400 MB that every TypeScript run would pull and never touch,
and the image would be rebuilt for a Java advisory that cannot reach a TypeScript build. The
weightier objection is the one security.md already makes: Gradle, Maven and `setup.py`
execute code from the package being built. Putting them beside each other means the blast
radius of one hostile spec is every toolchain in the image, rather than the one its own
language needs.

So: a shared base stage carrying .NET and the runner — Kiota travels inside it as a library
rather than as a tool — and one thin image per language on top of it. Nothing new is needed
to route to them — a run records its target's language
when it is queued (`PipelineRun.TargetLanguage`), and the API already starts one job per run
with `--run <id>`, so the dispatcher would pick the definition the same way it picks the id. The cost
is N images to keep current, which the shared base bounds.

Nothing about the claim needs a language filter: the dispatcher reads the run's recorded
language and starts the definition that matches, so an image is only ever handed a run it can
build. That was not true of a queue that drained whatever it found, which is one more thing
removing that mode bought.

## Who creates what

Two tools, and the split is not a matter of taste: one converges a declared state, the other
performs an ordered sequence, and each is bad at the other's job.

**Terraform owns the cloud resources.** Managed Postgres and its two roles, the Object Storage
buckets, the IAM applications and keys, the Key Manager key, the API container, and every job
definition — today that is `spacecraft-pipeline` and nothing else. `scaleway_job_definition` carries `image_uri`, `startup_command`, `args`, `env`,
`cpu_limit`, `memory_limit`, `local_storage_capacity`, `timeout`, `cron` and
`secret_reference`, which is the whole surface this deployment needs.

This is not tidiness. The run trigger assumes a job definition exists, and the API assumes an
IAM key allowed to start jobs exists. Nothing in the repository
creates any of them, and what nobody declares, nobody recreates.

**GitHub Actions owns the release.** Build the images, build the migration bundle, apply it,
then move the container and job definitions to the new image. That is an ordered sequence with
a dependency on the application version — Terraform has no primitive for "run this, wait for
exit 0, then update that", and `null_resource` with `local-exec` is not one either.

`image_uri` lives on the job definition and `StartJobDefinition` cannot override it — only the
command, the arguments and the environment. So a release must update the definition, which
means the pipeline passes the new tag to Terraform rather than Terraform being run by hand.
The alternative, a mutable tag repushed each release, keeps Terraform out of the deploy path
at the cost of a definition that no longer says which image it runs.

## Order of operations for a release

1. Verify: build, test, and `dotnet ef migrations has-pending-model-changes`. On EF Core 9 and
   later a model that has drifted from the last migration makes the bundle throw, and this is
   the difference between finding that on a pull request and finding it mid-deployment.
2. Build and push both images.
3. Generate the migration bundle and apply it to completion.
4. Move the API container and the job definitions to the new images.

Steps 3 and 4 are separate jobs with a `needs:` between them, which is the point: the ordering
was documented and manual before, and a deployment that skipped the migration produced an API
that started cleanly, reported healthy, and threw `column does not exist` on the first request
that touched a new column.

`Database:MigrateOnStartup` stays `false` outside Development, so nothing migrates implicitly.

## Migrations

The schema is applied by a **migration bundle** — a single-file executable produced by
`dotnet ef migrations bundle`, generated in CI and run from the deployment pipeline. It needs
neither the .NET SDK, nor the EF tools, nor the project's source code at execution.

```bash
ASPNETCORE_ENVIRONMENT=Production \
    dotnet ef migrations bundle --self-contained --target-runtime linux-x64 \
    --output artifacts/efbundle

ASPNETCORE_ENVIRONMENT=Production \
    ./artifacts/efbundle --connection "$MIGRATION_CONNECTION"
```

The environment is set at **both** steps deliberately: EF's design-time tooling defaults to
`Development`, which would have the bundle read development user secrets.

Rolling back is the same executable with a target: `./efbundle <PreviousMigration>`, or `0` to
revert everything. It runs the `Down` of every migration newer than the target, so it can lose
data — review before using it on production data.

A bundle cannot show the SQL it will run. When a change needs review before it is applied,
generate a script instead — `dotnet ef migrations script --idempotent` — read it, and keep it
as the release artifact.

**The migration identity is not the application identity.** Postgres carries two roles: one
that can change the schema, used only by the bundle, and one that can only read and write
rows, used by the API container and every job definition. This is the one objection to
runtime migration that no tooling choice removes on its own, and it is stated three times in
EF Core's own guidance. Without it the credential that serves the internet — and the one
inside which a customer's transitive npm dependencies execute — can drop a table.

That is also why the runner image cannot apply migrations at all: the mode was removed with
the decision above. Development applies them at API startup through
`Database:MigrateOnStartup`, and everywhere else it is the bundle. Nothing that runs a
customer's build has ever needed schema rights, and now nothing that runs it can ask for
them.

### Reaching the database from CI

Serverless Jobs cannot join a Private Network — Scaleway's own FAQ says so plainly: *"Scaleway
Serverless Jobs does not currently support Scaleway VPC or Private Networks, though this
feature is under development."* The database therefore already answers on a public endpoint,
firewalled to Scaleway's published IP prefixes, which is every Scaleway customer.

Running the bundle from a GitHub runner widens that allow-list to GitHub's ranges. It does not
open a boundary that was closed — there is no private path to widen — but it is a real
extension, and the migration job belongs behind a GitHub Environment with required reviewers
for that reason. A self-hosted runner inside the project is the alternative when that is not
acceptable.

### What is not automated yet

`release.yml` builds, tests and pushes the two images, and stops. The bundle, its application
and the Terraform apply are described above and not yet written; until they are, steps 3 and 4
are manual and their ordering rests on whoever runs them.

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

# Auth — validated at startup, at least 32 characters
Jwt__SigningKey=…
Jwt__Issuer=spacecraft
Jwt__Audience=spacecraft-api

# Key protection — startup fails without these outside Development, and the API and the
# runner must agree: the API encrypts registry credentials, the runner decrypts them, and
# they share one key ring. SecretKey is the one secret that must not live in the database
# it protects, so it comes from the deployment and nowhere else.
KeyProtection__Provider=ScalewayKms
KeyProtection__Kms__BaseUrl=https://api.scaleway.com
KeyProtection__Kms__Region=fr-par
KeyProtection__Kms__KeyId=…
KeyProtection__Kms__SecretKey=…

# Scaleway account — shared by every service that talks to it

# Object storage — its own IAM key, distinct from the email one
ObjectStorage__BucketName=openapi-bucket
ObjectStorage__ServiceUrl=https://s3.fr-par.scw.cloud
ObjectStorage__Region=fr-par
ObjectStorage__AccessKey=…
ObjectStorage__SecretKey=…
# The runner's own appsettings.json ships Region=us-east-1 and ForcePathStyle=true, where the
# API's ships fr-par and false. Both have to be set on the runner job or it addresses Scaleway
# path-style, in a region that is not the bucket's.
ObjectStorage__ForcePathStyle=false

# Email — startup fails without these outside Development
# SMTP rather than a provider API: the relay is swappable without touching code, and
# nothing below is derived from one. Scaleway's username is the id of the project the
# sending domain belongs to.
Email__Smtp__Host=smtp.tem.scaleway.com
Email__Smtp__Port=587
Email__Smtp__Security=StartTls
Email__Smtp__Username=…
Email__Smtp__Password=…
Email__Smtp__FromEmail=noreply@yourdomain
Email__Smtp__FromName=SpaceCraft

# Pipeline — steps run in the worker's own process, with no sandbox between a tenant's
# build and the host. Startup refuses without this outside Development, so that it is a
# decision written down rather than a default nobody read. There is no companion setting
# that turns a sandbox on — none exists. This acknowledges the absence; it selects nothing.
Pipeline__AllowUnsandboxedExecution=true

# Pipeline — the runner image sets this as an ENV, override only to move it
Pipeline__WorkspaceRoot=/tmp/spacecraft-workspaces
# Where dependencies are resolved from. Publishing goes wherever each credential says.
Pipeline__DependencyRegistry=https://registry.npmjs.org

# Public URLs and CORS
PublicUrls__Api=https://api.yourdomain
PublicUrls__App=https://app.yourdomain
Cors__AllowedOrigins__0=https://app.yourdomain

# Rate limits (optional; these are the defaults)
RateLimits__GlobalPermitsPerMinute=300
RateLimits__CredentialPermitsPerWindow=10
RateLimits__CredentialWindow=00:15:00
```

Startup fails fast rather than running degraded: on a signing key that is missing or shorter
than 32 characters, on mail that is unconfigured or half configured outside Development, on
`Email:Smtp:Security=None` there, on a port and a handshake that contradict each other, on
`KeyProtection:Provider` left at `None` outside Development, and on
`Pipeline:AllowUnsandboxedExecution` not being `true` there.

Key protection goes one step further and proves itself before anything is served. A hosted
service runs one real encrypt-and-decrypt against Key Manager on boot and refuses to start
unless the same bytes come back, because Data Protection does not fail the way one would hope:
a decryptor that throws is retried, then the key is dropped and a fresh one minted, and the
process serves traffic normally while every stored registry credential has quietly become
unreadable.

Nothing is checked about the generator, because there is nothing to check at runtime: which
generator runs is not configurable at all. Kiota is a compile-time dependency pinned by
`KiotaVersion` in `Directory.Packages.props`. What generation does read from configuration is
`Pipeline:GenerateTimeout` and `Pipeline:WorkspaceRoot`, and neither can select a different
generator or a different version of one.

The relay's certificate is validated in full. Revocation is always requested; what
`Email__Smtp__RequireCertificateRevocation` decides is what happens when no answer comes back,
which on macOS and in a container with no route to an OCSP responder is every time.

It is **on** for a deployment: a host that cannot get an answer about its relay's certificate
should say so rather than shrug. `appsettings.Development.json` turns it off, because .NET on
macOS never completes the check and a valid certificate is rejected — turn it off in a
container too if it has no route to a responder, and know what that buys you.

Nothing else moves either way: an expired certificate, an untrusted root, a mismatched name
and a revocation that comes back positive are refused in both modes.

`Email:Smtp:Security` is `StartTls`, `ImplicitTls` or `None`. `None` sends the password and
every message in the clear and is **refused outside Development**, where it exists for a relay
on the same machine with no certificate. It has to be written down — no missing setting can
ever mean plaintext, and MailKit's opportunistic `Auto` is deliberately not offered, since it
fails open on exactly the connection that matters.

`Email:Smtp:Port` and `Security` have to agree — 465 and 2465 expect TLS from the first
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
application, so a key that can send mail cannot read the bucket and vice versa. Both belong to
the same Scaleway project, but nothing here needs to know its id: the mail relay takes it as
an SMTP username like any other credential, and the bucket authenticates with its own key pair.

**A bucket per environment.** Development and production use separate buckets, created once in
the console; nothing creates them at startup. Scope the development key to the development
bucket, so a wrong `BucketName` there fails instead of writing into production.

Objects are laid out by ownership:

```
orgs/{org}/projects/{project}/specs/{name}-{spec}.yaml
orgs/{org}/projects/{project}/runs/{run}/artifact/{fileName}
orgs/{org}/projects/{project}/runs/{run}/logs/{step}.log
```

One prefix per project is what makes a per-project lifecycle rule or bulk delete possible.
Changing the layout later means moving what is already stored, and nothing in the repository
does that — decide the shape before there is anything in a production bucket.

### Secrets belong in files, not in the environment

With no Private Network available, Scaleway's own recommendation for Serverless Jobs is Secret
Manager. `scaleway_job_definition` takes a `secret_reference` block that mounts a secret as a
**file** rather than as an environment variable, and that difference is worth taking.

Pipeline steps run in the runner's own process, so a customer's transitive npm dependency runs
as the same user, in the same PID namespace. Everything in that process's environment —
`KeyProtection__Kms__SecretKey`, the storage keys, the connection string — is readable from
`/proc/self/environ` by anything the build executes. A file is not containment against the same
uid, but it is no longer handed over by simply existing.

It costs one block per secret in the job definition and one line of code, and the line is not
optional. The .NET default host reads `appsettings`, the environment and the command line —
nothing else. A file mounted at a path it was never told about is not configuration, so both
hosts call:

```csharp
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
```

The file name is the configuration key with the same double underscores the environment uses, so
`/run/secrets/ConnectionStrings__Postgres` becomes `ConnectionStrings:Postgres` and the settings
block below transposes to file names unchanged. `optional: true` keeps development and the test
hosts working, where the directory does not exist.

Remove that line and the job does not run on stale values — it fails its startup validation
naming the key it could not find, which is the right way for this to break.

The API's secrets stay environment variables. The argument for files is that a customer's build
runs inside the runner's process; nothing runs inside the API but the API.

Two prerequisites are not configuration and cannot be worked around:

- **The sending domain must be verified** in the Scaleway console (SPF, DKIM, MX) or every
  email is rejected.
- **The Data Protection key ring lives in Postgres**, shared by the API that encrypts
  registry tokens and the runner that decrypts them to publish. A ring local to each
  container meant the runner could not read what the API wrote, which surfaced as "the key
  was not found in the key ring" in the middle of a publish, after a build had succeeded.
  It is **wrapped at rest**: `KeyProtection:Provider=ScalewayKms` installs
  `ScalewayKmsXmlEncryptor` as Data Protection's `XmlEncryptor`, so each key reaches Postgres
  already encrypted under a Key Manager key the database does not hold. Leaving the provider
  at `None` outside Development is refused at startup rather than tolerated.

  Wrapping applies to keys minted after it is switched on, not retroactively — which sounds
  like a gap and is not one here, because there is no way to reach production with an
  unwrapped ring. Startup refuses outside Development unless a provider is configured, so the
  very first key a deployed ring ever holds is already wrapped. The retroactive case only
  arises on a development stack, where the ring protects nothing worth keeping.

## Starting a run

Nothing else reads the queue, so the API starts the job itself. `RunDispatcher` sweeps runs
that are still queued, claims a batch with `FOR UPDATE SKIP LOCKED`, and calls
`POST /serverless-jobs/v1alpha2/regions/{region}/job-definitions/{id}/start` with
`args: ["--run", "<guid>"]`. Serverless Jobs offer a cron trigger and nothing else — no queue
trigger, no event trigger — so this call is the only way.

This is why the API cannot scale to zero: the sweep is what starts a job, so an instance has
to be up for anything to run. See [The API container](#the-api-container).

There is deliberately no call from the request handler. A start that fails inline leaves a run
queued and never started, which is the same defect as having no trigger at all, only rarer and
therefore harder to see. One sweep is one code path, and a refusal is retried by the next.

```
PipelineJobs__BaseUrl=https://api.scaleway.com
PipelineJobs__Region=fr-par
PipelineJobs__JobDefinitionId=…
PipelineJobs__SecretKey=…            # IAM key allowed to start this job definition
```

That key is the platform's only control-plane credential, and it belongs to the API alone —
never to a job definition. Everything else the deployment holds reads or decrypts data; this
one starts compute, and a build running a customer's transitive npm dependencies must not be
able to reach it. It is why the code lives under `SpaceCraft.Api/Jobs` rather than in the
shared project.

`dispatched_at` on the run records that a job was asked for. It is a column rather than
memory because several API instances sweep the same table: without it each starts its own job
for the same run, which claiming makes harmless and the parallel-run quota makes expensive.

## Latency to expect

Serverless Jobs are guaranteed to reach *Running* within ten minutes of being triggered.
Typical start is far quicker, but the SLA is the number to design against: the UI must
distinguish "queued" from "running", or a slow start looks like a failure. Anything needing an
immediate answer — spec validation on upload — stays in the API, synchronously, where it
already is.
