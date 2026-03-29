# CanhoesBE

Backend for **Canhoes do Ano**.

## Runtime entry point

The real application runtime is:

- `Canhoes.API/Program.cs`

The repository root also contains older scaffold files and experiments. They
are not the primary runtime used by the frontend.

## Main API areas

- `v1/events/*` for event-scoped overview, admin, voting, wishlist and feed
- `canhoes/*` as the legacy compatibility layer still used by some frontend modules
- `hub/*` for the social feed
- `uploads/*` for media serving and fallback resolution
- `me` and `users` for authenticated user context

## Auth

- Frontend sends `Authorization: Bearer <Google id_token>`
- ASP.NET validates the Google token with JwtBearer
- `UserContextMiddleware` maps the Google identity to a local `UserEntity`
- The database is the source of truth for `IsAdmin`
- `GET /api/me` returns the authenticated backend profile used by the frontend

## Startup caveat

`Canhoes.API` still performs legacy schema/bootstrap work during startup. That
area is operationally sensitive and should be changed carefully.

## Commands

```bash
dotnet build Canhoes.API/Canhoes.Api.csproj
dotnet run --project Canhoes.API/Canhoes.Api.csproj
```
