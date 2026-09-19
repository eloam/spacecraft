# Security notes

SpaceCraft compiles and publishes code derived from untrusted OpenAPI documents. What a spec
may do, what the built code may reach and who may see what are the boundaries below.

## The spec is untrusted input

| Risk | Mitigation | Where |
|---|---|---|
| SSRF via `$ref: http://169.254.169.254/…` | External reference resolution disabled | `OpenApiSpecValidator` |
| YAML anchor expansion | 30 s parse timeout, 10 MB size cap | `SpecLimits` |
| Pathological recursion | Same timeout; failure is a validation result, never an exception | `OpenApiSpecValidator` |
| Enormous documents | 5,000 operation and 10,000 schema ceilings | `SpecLimits` |

## Pipeline isolation

Steps run as child processes of the runner, on the host, as the runner's own user (uid
10001): no container per step, no network namespace, no resource ceiling. `NetworkPolicy` on
a command records intent and is enforced by nothing. What holds:

- Generation runs in-process against an `HttpClient` that throws on every request.
- Every `npm` invocation runs with `--ignore-scripts`, in a child environment emptied first
  and refilled with `PATH`, `HOME`, `LANG`, `LC_ALL`, `TMPDIR` and npm's own settings — the
  platform's secrets are not there to inherit.
- Each command has a wall-clock timeout, and its output is scrubbed before it is stored.

The isolation relied on is the job's: one Serverless Job execution per run, torn down
afterwards. Outside Development startup refuses without `Pipeline:AllowUnsandboxedExecution=true`
— an acknowledgement, not a control; there is no sandbox to turn on.

| Control | Available |
|---|---|
| No egress during generation | yes, from the offline `HttpClient`, not a namespace |
| No egress during build | no |
| Dropped capabilities, read-only rootfs | whatever the platform provides |
| CPU / memory / PID ceilings | per job, not per step |
| Blast radius of a hostile build | the whole job |

Defensible for TypeScript, where `--ignore-scripts` is the control that matters. Gradle,
whose build file *is* code, or Python's `setup.py` mean one job per step.

## Credentials

- The build holds no credentials. Only the publish step decrypts a token, for one command,
  into the child environment — never on the command line — where a generated `.npmrc` refers
  to it by name.
- Tokens are Data-Protection-encrypted at rest; the API exposes a label and the last four
  characters, nothing more.
- Step output is scrubbed of every known secret before storage, and withheld if scrubbing
  times out.
- A registry address is refused unless it round-trips through `Uri` unchanged
  (`RegistryEndpoints.IsValid`), because it is written into the `.npmrc` npm expands.

### The key ring

The Data Protection key ring lives in Postgres (`spacecraft.data_protection_keys`), shared
by the API and the runner, and every key is wrapped by a Scaleway Key Manager key before it
is written; the one unwrapped secret is `KeyProtection:SecretKey`, which comes from the
deployment. Outside Development startup refuses without a provider or with a plaintext URL;
Development may run the fake Key Manager or, with `Provider=None`, an unwrapped ring under a
logged warning. On boot a hosted service round-trips a probe and unwraps every stored key,
and fails the start if any cannot be read — Data Protection would otherwise discard it
silently and mint a new one.

## Tenancy

Membership is checked in the database on every request, never taken from a claim. A
non-member, or an organization that does not exist, gets 404; a member with too low a role
gets 403 naming the role — both from `OrganizationAccess.DenyUnlessAsync`. Generating needs
Member, publishing Admin. Artifacts, step logs and spec sources are streamed by the API
under the caller's role, never handed out as bucket URLs.

## Auth

Access tokens live 15 minutes, refresh tokens 14 days; a refresh token rotates on every use
and a reused one revokes its whole family. Login answers 401 identically for an unknown
address and a wrong password; ten failures on a registered address answer 423 for fifteen
minutes. Every unauthenticated credential endpoint — register, login, forgot-password,
reset-password, waitlist — is rate-limited per address and per endpoint
(`RateLimits:CredentialPermitsPerWindow` over `CredentialWindow`).

## Password reset

A reset token is 256 CSPRNG bits, stored as a SHA-256 hash, valid one hour, single use, and
superseded by any newer request. `forgot-password` validates and enqueues, nothing else —
the lookup, the token and the mail happen on a background worker — so neither the answer nor
its timing says whether the address exists. The waitlist works the same way. `reset-password`
gives one answer for every unusable token, reports only password-policy failures (the token
stays valid), and revokes every session of the account.

## Mail

`SmtpEmailTransport` never logs a message body — a reset link is a bearer credential — but
logs the recipient in full on success and failure. Delivery retries transport errors and 4xx
with jittered backoff under per-attempt and total timeouts; 5xx and refused credentials are
not retried. Outside Development startup refuses `Smtp:Security=None` and refuses to fall
back to the logging transport.

## Deleting things

Spec versions: Member. Targets, projects, credentials: Admin. Organizations: Owner, with the
name typed back. Runs, credentials and publications carry ids without a foreign key, so the
per-slice deleters remove them and their blobs explicitly; a blob that cannot be removed is
logged and the delete continues. Deleting an organization, a project, a target or a spec
version is refused (409) while a run in scope is queued or running.

## Where a package goes

The registry address is on the credential, the credential is chosen on the SDK target, and a
target with none publishes nowhere. A run records the address when it is queued and refuses
to publish if the credential has moved since. Configuration never decides the destination,
in any environment — keep development credentials pointed at a local registry.
`Pipeline:DependencyRegistry` is only where `npm install` resolves dependencies.

## Known gaps

- Refresh tokens sit in `localStorage`. An httpOnly `SameSite=Strict` cookie with the access
  token in memory is stronger and needs CSRF handling on the API.
- Mail and reset requests wait in bounded in-memory channels that drop on overflow and on
  restart.
- The GitHub webhook receiver does not exist. When built it must verify
  `X-Hub-Signature-256` and use an installation token.
- No per-organization cap on runs: one tenant can saturate the platform's parallel-run quota.
- Maven Central is not a drop-in: a verified namespace and GPG signing are their own project.
