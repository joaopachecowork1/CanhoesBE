# Copilot Instructions

## Scope
- This repository is a .NET 10 backend solution centered on `Canhoes.API`.
- Keep changes simple, ready to deliver, and easy to review in pull requests.

## Build, Run, Test
- Restore: `dotnet restore`
- Build solution: `dotnet build Canhoes.slnx`
- Run API: `dotnet run --project Canhoes.API/Canhoes.Api.csproj`
- Run tests: `dotnet test Canhoes.Tests/Canhoes.Tests.csproj`

## Architecture Boundaries
- Main runtime and API surface are in `Canhoes.API`.
- `Canhoes.API/Controllers` uses partial controllers split by concern (`*.Admin.cs`, `*.MemberExperience.cs`, `*.Mappers.cs`, `*.Support.cs`). Continue this pattern when adding endpoints.
- Event-scoped contract lives under `api/v1/events` in `EventsController` partials. Prefer extending this contract over introducing new legacy routes.
- Data access is EF Core via `Canhoes.API/Data/CanhoesDbContext.cs` and entities in `Canhoes.API/Models`.

## Coding Conventions
- Prefer minimal, focused edits in existing files and preserve public API contracts.
- Reuse existing DTOs in `Canhoes.API/DTOs` and mapping helpers in controller mapper partials where possible.
- Keep endpoint behavior aligned with existing paging/result shapes used in admin and member routes.
- Preserve middleware ordering and startup conventions in `Canhoes.API/Program.cs`.

## Security and Configuration
- Never expose secrets (passwords, API keys, client secrets) in tracked config files.
- Use environment variables and local-only configuration for secrets (for example development-only config files excluded by gitignore).
- Keep mock auth strictly development-only; do not enable it in non-development environments.
- Prefer existing connection string keys and environment variable pattern (`ConnectionStrings__Default`).

## Testing Guidance
- Add or update tests in `Canhoes.Tests` for behavior changes.
- Follow existing test style (xUnit + FluentAssertions + EF InMemory) and helpers in `Canhoes.Tests/TestSupport.cs`.
- Favor contract/endpoint-shape assertions for API response changes.

## Key References
- Project overview and API contract notes: [README.md](../README.md)
- Solution projects: [Canhoes.slnx](../Canhoes.slnx)
- API startup and middleware pipeline: [Canhoes.API/Program.cs](../Canhoes.API/Program.cs)
- Event-scoped controller root: [Canhoes.API/Controllers/EventsController.cs](../Canhoes.API/Controllers/EventsController.cs)
- Legacy canhoes controller root: [Canhoes.API/Controllers/CanhoesController.cs](../Canhoes.API/Controllers/CanhoesController.cs)
- Database model configuration: [Canhoes.API/Data/CanhoesDbContext.cs](../Canhoes.API/Data/CanhoesDbContext.cs)
- Test project and examples: [Canhoes.Tests/Canhoes.Tests.csproj](../Canhoes.Tests/Canhoes.Tests.csproj), [Canhoes.Tests/ContractTests.cs](../Canhoes.Tests/ContractTests.cs)