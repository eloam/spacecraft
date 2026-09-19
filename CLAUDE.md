# SpaceCraft — working rules

## Principles
- Industry conventions only, never invent a name, layout or mechanism. Unsure? Look it up (learn.microsoft.com, dotnet/eShop, the pattern's canonical source) before proposing.
- Quality bar: as if Microsoft wrote this backend. Framework mechanism over home-grown (`Options` + DataAnnotations, `IHostedService`, endpoint filters, `IDesignTimeDbContextFactory`).
- Code, comments and commits in English; conversation in French.

## Architecture
- `SpaceCraft.Api` is vertical slices: `Features/<Name>/` owns everything about a feature.
  - `<UseCase>.cs` — `public static class RemoveMember { HandleAsync(...) }` plus the private helpers only it uses. One file per use case.
  - `<Name>Endpoints.cs` — the route group and `Map*` calls, nothing else.
  - `Requests/<Name>Request.cs`, `Responses/<Name>Response.cs` — one record per file, namespace = folder.
  - What several use cases share lives beside them under a name for what it is (`Sessions`, `MemberQueries`, `OrganizationDeleter`). `Common/` is cross-feature only; `Email/` is mail infrastructure only.
- `SpaceCraft.Infrastructure` = what both hosts (API + Runner) need. Pure rules go to `SpaceCraft.Domain`.
- Handlers do not log; services do. Handlers keep only state checks (database, Identity, membership).
- Several API instances run at once: never coordinate in process memory, claim rows in Postgres.

## Requests and validation
- Every request's shape rules live in a nested `public sealed class Validator : AbstractValidator<T>` inside the record (FluentValidation). `RuleLevelCascadeMode = CascadeMode.Stop` first; every rule has `.WithMessage(...)` with our own text — no library default reaches a client.
- Validators are found by `AddValidatorsFromAssemblyContaining<Program>()` and run by `Common/ValidationFilterFactory` on the `/api/v1` group. A record with no shape rule has no `Validator`.
- Error keys are the property names (PascalCase); the web client reads `errors.Email` case-sensitively. Never `OverridePropertyName`.
- Judge the value as the handler stores it (trimmed). A route that can answer 400 declares `.ProducesValidationProblem()`.
- Record names are OpenAPI schema ids the web client is generated from — never rename them. After a contract change: `npm run gen:api` in `src/web` (needs the API running).
- Options classes keep DataAnnotations + `ValidateDataAnnotations().ValidateOnStart()`.

## Configuration
- No hardcoded value, no `?? literal`: a missing setting throws at startup naming the key. Deployment-dependent options are `string?` + `[Required]` with no initializer; tuning knobs may have defaults but no dead `[Required]`.
- Read settings through `IOptions<T>`; reading `IConfiguration` eagerly is legitimate only when a registration branches on it.
- `appsettings.json` (base files only): every section with required settings opens with a `"//"` key listing them, one level deep (`Required: …`, `Required outside Development: …`). No `"//"` elsewhere, none in Development files, no explanatory keys.

## Build and tests
- `dotnet build` must end with 0 warnings (`TreatWarningsAsErrors`, analyzers on).
- Unit tests: `dotnet run --project tests/SpaceCraft.UnitTests` (xunit v3, not `dotnet test`).
- Integration tests need Docker (`tests/SpaceCraft.IntegrationTests`); CI runs them.
- EF: `dotnet ef migrations has-pending-model-changes --project src/SpaceCraft.Infrastructure --startup-project src/SpaceCraft.Api` must report none after touching entities.

## Git
- Never commit on your own initiative; commit only when asked. One commit per agreed point.
- Commit body: plain prose — what changed, why, what stays — ending with the doc or reference app that makes it the convention.
- Never stage `infra/terraform/` (`git add -A -- ':!infra/terraform'`).
