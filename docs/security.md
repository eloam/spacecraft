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

**Deployment requirement:** the Data Protection key ring must be persisted to shared storage
and wrapped by a KMS key. The default is per-machine and ephemeral in a container — losing it
makes every stored token unreadable; leaking it makes them all readable.

## Tenancy

Membership is checked against the database on every request rather than trusted from a JWT
claim, so removing someone takes effect immediately instead of when their access token
expires. A non-member and a non-existent organization are reported identically (404), so the
API cannot be used to probe which organizations exist.

Publishing requires a higher role than generating: `Admin` versus `Member`. Pushing to a
public registry cannot be undone.

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

Every unauthenticated credential endpoint — register, login, forgot-password, reset-password —
is rate limited per IP **and per endpoint**. A single shared budget was tried first and was
wrong: two mistyped sign-ins followed by a reset request exhausted it, and an office behind
one NAT address exhausted it faster. Separate windows still stop spraying a single endpoint.

## Known gaps

- Refresh tokens are stored in `localStorage`, which is readable by any script running on the
  origin. An httpOnly `SameSite=Strict` cookie is stronger and needs CSRF handling.
- Password reset emails go through `LoggingEmailSender`, which writes them to the log. Until
  a real transport is wired in, reset links are visible to anyone who can read application
  logs — acceptable in development, not beyond it.
- The GitHub webhook receiver is not built. When it is, it must verify the
  `X-Hub-Signature-256` HMAC and use a GitHub App installation token, not an OAuth token.
- There is no per-organization quota on pipeline runs; a tenant can currently occupy every
  worker slot.

## Running without nested containers

`SandboxMode.PlatformIsolated` executes pipeline steps in-process instead of in throwaway
Docker containers, for hosts where a Docker daemon is unavailable — a serverless job, a
single-use pod, a microVM. The isolation is then the platform's, not ours, and that is a
real change to the threat model rather than a configuration detail:

| Control | `Container` | `PlatformIsolated` |
|---|---|---|
| `--network none` during generate and build | yes | not available |
| Dropped capabilities, read-only rootfs | yes | whatever the platform provides |
| CPU / memory / PID ceilings per step | per step | per job, set by the platform |
| Blast radius of a hostile build | one container | the whole job |

What still holds in both: `npm install --ignore-scripts`, no registry credentials during
build, output scrubbing, and a wall-clock timeout per command.

This is defensible for TypeScript, where `--ignore-scripts` is the control that matters. It
becomes a real decision for Gradle — whose build file *is* executable code — and for Python's
`setup.py`. Before adding those languages on a platform-isolated host, confirm what the
platform actually guarantees, in particular whether egress can be denied.

`SandboxMode.None` is the same execution path with no such assertion, and startup refuses it
outside Development.
