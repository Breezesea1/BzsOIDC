# AGENTS.md
Repository-specific guidance for coding agents working in **BzsOIDC**.
Favor small, verifiable changes and match existing local patterns.

## 1. Repository shape
```text
BzsOIDC/
├── src/
│   ├── BzsOIDC.AppHost/                 # Aspire AppHost
│   ├── BzsOIDC.AppHost.ServiceDefaults/ # OTEL, health, defaults
│   ├── BzsOIDC.Idp/                     # ASP.NET Core + Blazor IDP
│   ├── BzsOIDC.Idp.Client/              # client/shared UI services
│   ├── BzsOIDC.Idp.Migrator/            # DB migration executable
│   └── Shared/
│       └── BzsOIDC.Shared.Infrastructure/
├── tests/
│   ├── BzsOIDC.Idp.UnitTests/           # xUnit + NSubstitute + bUnit
│   ├── BzsOIDC.Idp.IntegrationTests/    # xUnit + TestHost + SQLite
│   └── BzsOIDC.Idp.E2ETests/            # xUnit + Playwright + Aspire
└── BzsOIDC.sln
```
- Target framework: `net10.0`
- `Nullable=enable`, `ImplicitUsings=enable`
- Main stack: ASP.NET Core, Blazor, OpenIddict, EF Core, Aspire
- Frontend assets live in `src/BzsOIDC.Idp/` and use Tailwind CLI + GSAP copy script

## 2. Rule files
Present:
- `AGENTS.md`
- `Directory.Build.props` defines the local fallback application version; release CI overrides it from an `X.Y.Z` Git tag.

Not present when checked:
- `.cursorrules`
- `.cursor/rules/`
- `.github/copilot-instructions.md`
- `.editorconfig`
If any of those files are added later, update this document.

## 3. Standard commands
Run from the repo root unless noted otherwise.

### 3.1 Restore / build
```bash
dotnet restore BzsOIDC.sln
dotnet build BzsOIDC.sln
dotnet build BzsOIDC.sln -c Release
```

### 3.2 Format / lint verification
```bash
dotnet format BzsOIDC.sln --verify-no-changes --verbosity minimal
dotnet format BzsOIDC.sln --verbosity minimal
```

### 3.3 Run the app
Preferred distributed entrypoint:
```bash
aspire run
```
This is also what the E2E fixture launches.

Run only the IDP directly:
```bash
dotnet run --project src/BzsOIDC.Idp/BzsOIDC.Idp.csproj
```

### 3.4 Frontend asset commands
Run in `src/BzsOIDC.Idp/` when needed:
```bash
npm install
npm run css:build
npm run css:watch
npm run gsap:copy
```
`BzsOIDC.Idp.csproj` already runs `css:build` and `gsap:copy` before `Build` and `Publish`.
`npm install` is not automatically re-run when `package-lock.json` already exists, so run it manually after dependency changes or on a fresh machine.

### 3.5 Database migrations
From `src/BzsOIDC.Idp/`:
```bash
dotnet ef migrations add <MigrationName> --context IdpDbContext
dotnet ef database update --context IdpDbContext
```

## 4. Test commands
### 4.1 Run all tests
```bash
dotnet test BzsOIDC.sln
dotnet test BzsOIDC.sln --no-build
```

### 4.2 Run one test project
```bash
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj
dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj
dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj
```

### 4.3 Run a single test
```bash
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj --filter "FullyQualifiedName=BzsOIDC.Idp.UnitTests.Controllers.PermissionScopesControllerTests.GetByPermission_WhenPermissionEmpty_ReturnsValidationProblem"
dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj --filter "FullyQualifiedName=BzsOIDC.Idp.E2ETests.AuthExperienceE2ETests.LoginPage_AllowsThemeAndLanguageSwitching"
```

### 4.4 Run a class or subset
```bash
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj --filter "FullyQualifiedName~PermissionScopesControllerTests"
dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj --filter "FullyQualifiedName~ConnectControllerIntegrationTests"
dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj --filter "FullyQualifiedName~AuthExperienceE2ETests"
```

### 4.5 Test notes
- Unit tests: xUnit + NSubstitute + bUnit
- Integration tests: ASP.NET Core `TestHost` + EF Core SQLite
- E2E tests: `Microsoft.Playwright.Xunit` + Aspire orchestration
- `Xunit` is supplied as a global using in each test csproj
- Trait/category filters are not part of the current suite; prefer `FullyQualifiedName`
- Local E2E execution expects the `aspire` CLI to be installed and available on `PATH`

## 5. Code style
Follow the surrounding file before applying generic .NET preferences.

### 5.1 Usings / imports
- Keep explicit usings at the top, one per line
- Match the local file's ordering style rather than re-sorting unrelated lines
- Remove unused usings
- Do not add redundant framework usings just because implicit usings are enabled

### 5.2 Formatting
- 4-space indentation
- Allman braces
- Preserve fluent-call alignment as seen in `AppHost.cs` and `Program.cs`
- Collection expressions like `[]` are already in use; prefer them when they fit

### 5.3 Naming
- Types, methods, properties: `PascalCase`
- Parameters and locals: `camelCase`
- Private fields: `_camelCase`
- Extension/helper classes: `*Extensions`
- Test classes usually end in `Tests`, with suffixes like `IntegrationTests` and `E2ETests` also used
- Test methods: `{Method}_{Condition}_{Expected}`

### 5.4 Types and null handling
- Respect nullable reference types; do not disable nullable checks
- Prefer early validation with `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, or early returns
- Use null-forgiving only when surrounding code already proves safety

### 5.5 Existing C# patterns
- Primary constructors are common in controllers, services, and test helpers
- Extension blocks are used in shared infrastructure (`MigrateDbContextExtensions.cs`)
- `internal sealed` is common for implementation classes; public controllers are often `sealed`

### 5.6 Async and cancellation
- Use `async`/`await` for I/O and framework calls
- Return `Task` / `Task<T>` rather than `async void`
- Pass `CancellationToken` through controller/service layers when supported

### 5.7 DI and configuration
- Register services through `IServiceCollection` helpers instead of scattering setup inline
- Follow patterns like `AddOptions<T>().Bind(...)`
- Keep `AddDbContext` and `AddDbContextFactory` aligned with current IDP registration
- Check `src/Shared/BzsOIDC.Shared.Infrastructure/` before adding new infra helpers

### 5.8 Error handling and logging
- Prefer descriptive exceptions; `InvalidOperationException` with clear context is common
- Use structured logging (`logger.LogInformation("... {Name}", value)`) instead of string concatenation
- Do not add empty catches in application code
- A narrow swallow-only readiness probe exists in E2E infrastructure; treat it as a special-case fixture pattern, not the default

### 5.9 Controllers, services, tests
- Controllers validate input early and return framework results immediately
- Services return domain results/DTOs rather than leaking infra types upward
- Use xUnit `Assert.*`; the repo does not use FluentAssertions
- Use NSubstitute for mocks/stubs in unit tests and some integration tests
- Keep tests in Arrange / Act / Assert order
- Use `[Fact]` by default; `[Theory]` + `[InlineData]` appear only where parameterization helps
If you touch auth, OIDC, migrations, startup wiring, or UI flows, add or run integration/E2E coverage as appropriate.

## 6. Agent workflow for this repo
1. Read the target area first; do not guess how services are wired.
2. Reuse helpers from `src/Shared/BzsOIDC.Shared.Infrastructure/` before creating new infra code.
3. After edits, verify with the smallest relevant set first:
```bash
dotnet build BzsOIDC.sln
dotnet format BzsOIDC.sln --verify-no-changes --verbosity minimal
dotnet test <affected project or filtered test>
```
4. For broader changes, finish with `dotnet test BzsOIDC.sln`.
5. If you changed frontend assets or UI classes under `src/BzsOIDC.Idp/`, make sure the CSS/asset pipeline still works.
Keep this file synchronized with the repo whenever projects, test layers, or rule files change.

## Agent skills

### Issue tracker

Issues and specs are tracked in GitHub Issues for `Breezesea1/BzsOIDC`.
See `docs/agents/issue-tracker.md`.

### Triage labels

Use the five canonical Matt flow labels.
See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository using root `CONTEXT.md` and `docs/adr/`.
See `docs/agents/domain.md`.
