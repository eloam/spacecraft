# Security notes

SpaceCraft compiles and publishes code derived from documents that arrive over the internet.
That makes two things load-bearing: what an OpenAPI document is allowed to do, and what the
code built from it is allowed to reach.

## Untrusted input: the spec

A specification is hostile input, not configuration.

| Risk | Mitigation | Where |
|---|---|---|
| SSRF via `$ref: http://169.254.169.254/…` | External reference resolution disabled | `OpenApiSpecValidator` |
| YAML anchor expansion ("billion laughs") | 30s parse timeout, 10 MB size cap | `SpecLimits` |
| Pathological recursion | Same timeout; failure is a validation result, never an exception | `OpenApiSpecValidator` |
| Enormous documents | 5,000 operation and 10,000 schema ceilings | `SpecLimits` |

The 10 MB cap is checked on the bound upload rather than before it is read. ASP.NET Core
buffers the multipart body first, bounded by the 12 MB `MultipartBodyLengthLimit` set in
`Program.cs`; the endpoint then refuses anything over 10 MB before the document is parsed or
stored, and the validator measures the stream again on its own way in, so a document reaching
it by another route is bounded too.

## Untrusted code: the build

`npm install` executes arbitrary lifecycle scripts written by whoever authored a package's
dependencies. Nothing contains that. There is one `ICommandRunner`, `LocalCommandRunner`, and
it starts each step as a child of the worker's own process, on the host, under the worker's
own user: no container per step, no dropped capabilities, no read-only root, no network
namespace, no memory or PID ceiling. `NetworkPolicy` on a command records what a step is
*meant* to reach and is read by nobody. What does hold is set out under "The pipeline is not
sandboxed" below.

`npm install` runs with `--ignore-scripts`, and so do build, pack and publish. Kiota's runtime
packages are pure TypeScript and need no lifecycle hooks, so refusing to run any is free — and
for TypeScript it is the control that matters, because there is nothing else between a hostile
dependency and the host.

Generation runs nothing at all. Kiota is a package reference of `SpaceCraft.Pipeline` rather
than a CLI, so it runs in the worker's own process against an `HttpClient` whose handler
(`KiotaSdkGenerator.OfflineHandler`) throws on every request: a document that tries to fetch
something gets an exception naming the URL, not a socket. That is the one place in the pipeline
where "no egress" is enforced rather than declared.

## Credentials

The **build step holds no credentials at all**. Only publish decrypts a token, and only for
the duration of one command.

- Tokens are encrypted with ASP.NET Core Data Protection before they touch the database.
  There is no plaintext column in the schema, and no endpoint returns a stored token — the
  API exposes only a label and the last four characters.
- The token is set in the child process's environment block by `LocalCommandRunner`, never on
  the command line: arguments are visible to every user on the host through the process list.
  There is no env file and no container between the two processes; the `.npmrc` npm reads
  names `${SPACECRAFT_NPM_TOKEN}` rather than the value, so a workspace that survives cleanup
  holds no secret.
- Captured output is scrubbed before it is stored, against both the known token and a set of
  patterns for tokens we were never told about. Redaction fails closed: if the scan times
  out, the output is withheld rather than persisted unscanned.

### The key ring

The Data Protection key ring is persisted to Postgres, so the API and the runner share it
without a filesystem between them, and **wrapped by a Scaleway Key Manager key** before it is
written. The wrapping is what makes the encryption mean anything: stored in clear, the ring
sat in the same database as the credentials it protects, and a single dump carried both the
ciphertext and the key that opens it. The only unwrapped secret now lives in the deployment —
`KeyProtection:Kms:SecretKey` — where a database dump cannot reach it.

With one caveat that matters on an existing deployment: an `IXmlEncryptor` applies when a key
is minted, not retroactively. Keys already in the ring when Key Manager was switched on stay
unwrapped, and every credential encrypted under one stays readable from a dump alone. That is
what `SpaceCraft.Runner --rewrap-credentials` is for, and until it has run the paragraph above
describes only the keys minted since.

Development points the same client at `infra/SpaceCraft.FakeScalewayKms`, a stand-in that
speaks Key Manager's documented routes, in the same spirit as MinIO for Object Storage and
Mailpit for SMTP. There is no "if development" branch: the code that wraps the ring in
production is the code that runs on a developer machine. Outside Development the application
refuses to start without a provider, and refuses a plaintext key-manager URL.

**Startup proves the key manager can still read what is stored**, not merely that it answers.
A fresh probe succeeds against a key manager holding a brand-new master key — restored from a
backup, or rotated destructively — while every key already stored was wrapped by the old one.
Left to run, Data Protection discards the unreadable key, mints a replacement, and carries on:
new credentials are protected normally while every earlier one is unreadable for good, and
nothing in the logs says so. Every stored key is therefore unwrapped at boot, and a failure is
a failed start.

## Tenancy

Membership is checked against the database on every request rather than trusted from a JWT
claim, so removing someone takes effect immediately instead of when their access token
expires. A non-member and a non-existent organization are reported identically (404), so the
API cannot be used to probe which organizations exist.

A member whose role is too low is answered differently: 403, naming the role the action
needs. The 404 mask exists to keep an organization id from being confirmed, and against
someone already reading that organization it conceals nothing while costing a true answer —
it told a viewer that the organization they were looking at did not exist. Both refusals are
issued from one place, `OrganizationAccessGuard.DenyUnlessAsync`, so they cannot drift apart.

Publishing requires a higher role than generating: `Admin` versus `Member`. Pushing to a
public registry cannot be undone.

Artifacts, step logs and spec sources are streamed by the API, never handed out as presigned
bucket URLs: a presigned URL is a bearer credential in a link, and it would require the
bucket to be reachable from a browser. The bucket has no public surface and needs no CORS
policy. All three reads require `Viewer`.

### The pipeline is not sandboxed

Steps run in the worker's own process, as child processes on the host. `NetworkPolicy` on a
command records what a step is *meant* to reach and enforces nothing — the note on the enum
says so. What actually holds:

- `--ignore-scripts` on every npm invocation, so a generated package's dependencies do not
  execute during install, build or publish.
- The child environment is emptied and refilled with only what the step needs. It used to be
  inherited whole, which put the Key Manager token — and therefore every organization's
  registry credentials — in reach of anything npm ran.
- Registry addresses must be identical to what they parse to. The address is written into the
  `.npmrc` a publish reads, and npm expands `${VAR}` there, so a newline in an address was a
  configuration line of the caller's choosing.

The isolation being relied on is the job's: one execution per container, torn down afterwards.
That holds only while a job runs a single run, and nothing enforces it.
`Runner:MaxRunsPerInvocation` defaults to 25, its range attribute accepts anything up to
10,000, and the only place that sets it to 1 is `compose.development.yaml` — a deployment that
leaves the default drains twenty-five tenants' runs through one process, sharing a filesystem
and whatever the previous run left behind. What startup does refuse is running unsandboxed at
all outside Development without `Pipeline:AllowUnsandboxedExecution`; the exception it throws
asks for `MaxRunsPerInvocation` to be kept at 1 without ever checking that it is.

## Auth

- Access tokens are short-lived (15 min); refresh tokens rotate on every use.
- Presenting a used refresh token revokes its entire family — the safe response to a leaked
  token, since the legitimate holder and the attacker are indistinguishable at that point.
- Login returns the same response for an unknown address and a wrong password, so one attempt
  cannot tell the two apart. Lockout is the exception, and it is worth naming: ten failures
  against a registered address turn the answer into 423 for fifteen minutes, which an address
  with no account never gets. Enumeration is expensive rather than impossible.
- Registration, login and waitlist signup are rate-limited per IP.

## Password reset

The reset token is a bearer credential that sits in a mailbox, so it is treated like one:
256 bits from the CSPRNG, stored only as a SHA-256 hash, valid for one hour, and single use.
Requesting a reset supersedes any outstanding token, so a forwarded older email stops working.

Two properties matter more than the token itself:

- **The flow is not an account oracle.** `POST /api/v1/auth/forgot-password` returns the same 202 and
  the same body whether or not the address is registered, and every way a token can be
  unusable — unknown, expired, spent, superseded — produces one identical rejection. A
  password-policy failure is the exception: that is the caller's own input, so it is safe to
  say what was wrong, and the token stays unspent so they can try again.
- **Resetting ends every existing session.** Whoever knew the old password may still hold a
  refresh token; a reset that left those alive would not actually recover the account. All
  outstanding refresh tokens are revoked as part of the change, and Identity's security stamp
  is rolled by going through `ResetPasswordAsync` rather than writing the hash directly.

**The endpoint does no work of its own.** `forgot-password` validates the address shape,
puts it on a queue, and returns. The waitlist signup endpoint works the same way, and for the
same reason — it had the identical flaw, measured at 3.5 ms for an already-confirmed address
against 7.5 ms for a new one, which answered "is this address already signed up?" despite the
deliberately identical response. Looking the account up and issuing a token happen on a
background runner. Identical responses are only half the defence: when the registered branch
also read the database and wrote a token while the unregistered branch returned immediately,
the two took 21.5 ms and 4.6 ms — a reliable account oracle in spite of identical bodies.
Measured again after the work moved off the request thread, medians were 0.71 ms and 0.81 ms
over 30 samples each, with the unregistered case sometimes the slower of the two.

Every unauthenticated credential endpoint — register, login, forgot-password, reset-password —
is rate limited per IP **and per endpoint**. A single shared budget was tried first and was
wrong: two mistyped sign-ins followed by a reset request exhausted it, and an office behind
one NAT address exhausted it faster. Separate windows still stop spraying a single endpoint.

## Mail delivery

SMTP, over MailKit. Two deployment prerequisites are not optional: the sending domain must be
one the relay has been shown to own (SPF, DKIM and MX records) or every send is rejected, and
`Email:Smtp:Host`/`Username`/`Password` must be set — outside Development startup fails
without them rather than quietly writing reset links to the log. `Email:Smtp:Security` of
`None` is refused outside Development for the same reason: a relay reached in the clear
carries the password and every reset link with it.

Message bodies are never logged by this transport. A reset link is a bearer credential, and
an error path that dumped the request body would put it in the log store anyway, which is
precisely what moving off the logging transport was meant to prevent. The recipient is not
treated the same way: `SmtpEmailTransport` logs the address in full, on the success path and
the failure path alike. Nothing masks it.

Retries with exponential backoff and jitter, a per-attempt timeout and a total timeout come
from a Polly `ResiliencePipeline` built in `EmailServiceExtensions`, and only transport-level
trouble is retried — a rejected recipient or a refused password is not. There is no circuit
breaker: not on this pipeline, and not anywhere else in the repository. The jitter matters
more than it looks: without it, every message queued during an outage stampedes the relay the
moment it recovers.

## Deleting things

Deletion is scoped by role: an SDK target or a project takes Admin, an organization takes
Owner. An organization also requires its name typed back, because that one act removes every
project, run, credential and publication record it holds.

Cascade constraints cover only part of the graph — projects, members, targets and specs hang
off their parent, but pipeline runs, registry credentials and publications carry an
organization id with no foreign key behind it. Deletion therefore removes those explicitly.
Leaving them would strand **encrypted registry tokens belonging to an organization that no
longer exists**, which is the leftover that actually matters.

Stored objects go too: specs, build artifacts and step logs are blobs whose only reference is
the row being deleted. A blob that cannot be removed is logged and the delete continues — a
wasted object is better than a half-deleted organization.

A delete is refused while any run in scope is queued or running, since a runner holding a
lease would carry on against rows that no longer exist and report its failure nowhere.

## Known gaps

- Refresh tokens are stored in `localStorage`, which is readable by any script running on the
  origin. An httpOnly `SameSite=Strict` cookie is stronger and needs CSRF handling.
- Mail queues are in memory. A process that stops with messages pending drops them; a user
  who receives no link asks again. A durable outbox is the upgrade if delivery ever has to be
  guaranteed rather than merely likely.
- In Development, with no transport configured, messages are written to the log **in full**,
  reset links included. That is how a developer follows the flow without a mail provider, and
  it is why startup refuses to fall back to it outside Development.
- The GitHub webhook receiver is not built. When it is, it must verify the
  `X-Hub-Signature-256` HMAC and use a GitHub App installation token, not an OAuth token.
- There is no per-organization quota on pipeline runs; a tenant can currently occupy every
  runner slot.

## Where pipeline steps run

Steps run in the runner's own process, with Node baked into its image. Kiota is not: it is a
package reference of `SpaceCraft.Pipeline`, restored into `/app` with everything else, so
`npm` — install, compile, pack, publish — is the only name the pipeline still resolves off
PATH. `tsc` is not among them: it comes out of the generated package's own `node_modules`,
which `npm` invokes.
The isolation is the job's: one execution per container, created for that run and destroyed
afterwards.

That assertion holds only while **one job runs one run**, and it is a convention rather than a
guarantee. Draining several runs through a single invocation puts other tenants' specs in the
same process, sharing `/tmp` and whatever the previous run left behind. Runs are triggered
individually and the development compose sets `Runner__MaxRunsPerInvocation=1` to match, but
the default in `appsettings.json` is 25 and the e2e compose sets 25 as well — a deployment
that does not set it to 1 does not have this property.

What the job does not give, that a container per step would:

| Control | Available |
|---|---|
| No egress during generation | yes, but from the offline `HttpClient`, not a namespace |
| No egress during build | no |
| Dropped capabilities, read-only rootfs | whatever the platform provides |
| CPU / memory / PID ceilings | per job, not per step |
| Blast radius of a hostile build | the whole job |

What holds regardless: `npm install --ignore-scripts`, no registry credentials during build,
output scrubbing, and a wall-clock timeout per command.

This is defensible for TypeScript, where `--ignore-scripts` is the control that matters. It
becomes a real decision for Gradle — whose build file *is* executable code — and for Python's
`setup.py`. Adding either means moving to one job per step, so each gets its own container and
its own limits, and confirming what the platform guarantees about egress.

## Where a package is published

The destination is on the credential, not in configuration. A token only authenticates against
one registry, so the two travel together: an organization publishing to its own Verdaccio,
Nexus or Artifactory stores the endpoint beside the token that opens it.

Which credential a target uses is on the target, and a run refuses rather than choosing when
an organization has several usable ones. Before that it took the most recently created, so
adding a credential silently redirected every target in the organization — verified by
watching a package land on a registry nobody had picked for it.

This removes an accidental safeguard. Configuration used to decide the destination, so a real
npm token in a development database still published to the local registry. It no longer does —
what a credential says is where its packages go, in every environment. Keep development
credentials pointed at a local registry, and never store a public-registry token in a database
you are willing to experiment with.

`Pipeline:DependencyRegistry` is a different thing and stays in configuration: it is where
`npm install` resolves the generated package's dependencies from, which must not follow a
customer's private registry.
