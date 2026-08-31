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

The upload endpoint enforces the size cap before buffering, so an oversized body is rejected
without being read into memory.

## Untrusted code: the build

`npm install` executes arbitrary lifecycle scripts written by whoever authored a package's
dependencies. Every pipeline step therefore runs in a throwaway container:

- `--network none` for generation and compilation; egress only for dependency install and publish
- `--cap-drop ALL` and `--security-opt no-new-privileges`
- `--read-only` root filesystem, writable only on a `noexec` tmpfs and the mounted workspace
- non-root user, bounded memory, CPU and PIDs
- a wall-clock timeout on every command

On top of that, `npm install` runs with `--ignore-scripts`. Kiota's runtime packages are pure
TypeScript and need no lifecycle hooks, so refusing to run any is free.

## Credentials

The **build step holds no credentials at all**. Only publish decrypts a token, and only for
the duration of one command.

- Tokens are encrypted with ASP.NET Core Data Protection before they touch the database.
  There is no plaintext column in the schema, and no endpoint returns a stored token — the
  API exposes only a label and the last four characters.
- The token reaches the container through a `0600` env file, never on the command line:
  arguments are visible to every user on the host through the process list.
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

## Auth

- Access tokens are short-lived (15 min); refresh tokens rotate on every use.
- Presenting a used refresh token revokes its entire family — the safe response to a leaked
  token, since the legitimate holder and the attacker are indistinguishable at that point.
- Login returns the same response for an unknown address and a wrong password, so the
  endpoint cannot enumerate accounts.
- Registration, login and waitlist signup are rate-limited per IP.

## Password reset

The reset token is a bearer credential that sits in a mailbox, so it is treated like one:
256 bits from the CSPRNG, stored only as a SHA-256 hash, valid for one hour, and single use.
Requesting a reset supersedes any outstanding token, so a forwarded older email stops working.

Two properties matter more than the token itself:

- **The flow is not an account oracle.** `POST /auth/forgot-password` returns the same 202 and
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

Scaleway Transactional Email, called at
`POST /transactional-email/v1alpha1/regions/{region}/emails` with the API key in
`X-Auth-Token`. Two deployment prerequisites are not optional: the sending domain must be
verified in the Scaleway console (SPF, DKIM and MX records) or every send is rejected, and
`Email:Smtp:Host`/`Username`/`Password` must be set — startup fails without them rather than
quietly writing reset links to the log.

Message bodies are never logged by this transport. A reset link is a bearer credential, and
an error path that dumped the request body would put it in the log store anyway, which is
precisely what moving off the logging transport was meant to prevent. Recipients are masked
to `y***@domain` so a delivery can still be correlated with a complaint.

Retries, exponential backoff with jitter, per-attempt timeouts and a circuit breaker come
from `Microsoft.Extensions.Http.Resilience`. The jitter matters more than it looks: without
it, every message queued during an outage stampedes the provider the moment it recovers.

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

Steps run in the runner's own process, with Kiota and Node baked into its image. The isolation
is the job's: one execution per container, created for that run and destroyed afterwards.

That assertion holds only while **one job runs one run**. Draining several runs through a
single invocation puts other tenants' specs in the same process, sharing `/tmp` and whatever
the previous run left behind — so runs are triggered individually, and the development
compose sets `Runner__MaxRunsPerInvocation=1` to match.

What the job does not give, that a container per step would:

| Control | Available |
|---|---|
| `--network none` during generate and build | no |
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
