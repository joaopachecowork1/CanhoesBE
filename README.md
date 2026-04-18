# Canhoes Backend

Backend ASP.NET Core para o módulo Canhões, com surface event-scoped em `api/v1/events`.

## Run

```bash
dotnet restore
dotnet build Canhoes.slnx
dotnet run --project Canhoes.Api
```

## Admin contract

O contrato canónico do admin é event-scoped e usa bootstrap leve + listas paginadas por secção.

### Bootstrap

- `GET /v1/events/{eventId}/admin/bootstrap?includeLists=false`
- Resposta: `events`, `state`, `counts`
- `includeLists` é aceite por compatibilidade, mas já não activa listas embebidas

### Leitura paginada

- `GET /v1/events/{eventId}/admin/nominations/paged`
  - shape: `nominations`, `total`, `skip`, `take`, `hasMore`
- `GET /v1/events/{eventId}/admin/votes/paged`
  - shape: `votes`, `total`, `skip`, `take`, `hasMore`
- `GET /v1/events/{eventId}/admin/members/paged`
  - shape: `items`, `total`, `skip`, `take`, `hasMore`
- `GET /v1/events/{eventId}/admin/official-results/paged`
  - shape: `items`, `total`, `skip`, `take`, `hasMore`

### Summaries

- `GET /v1/events/{eventId}/admin/categories/summary`
- `GET /v1/events/{eventId}/admin/nominees/summary`
- `GET /v1/events/{eventId}/admin/nominations/summary`

### Moderação

- `GET/PATCH/PUT/DELETE /v1/events/{eventId}/admin/category-proposals...`
- `GET/PATCH/PUT/DELETE /v1/events/{eventId}/admin/measure-proposals...`
- `POST /v1/events/{eventId}/admin/nominations/{nomineeId}/approve`
- `POST /v1/events/{eventId}/admin/nominations/{nomineeId}/reject`
- `POST /v1/events/{eventId}/admin/nominations/{nomineeId}/set-category`

### Mantidos fora desta limpeza

- `GET/PUT /v1/events/{eventId}/admin/state`
- `PUT /v1/events/{eventId}/admin/phase`
- `PUT /v1/events/{eventId}/admin/activate`
- `PATCH /v1/events/{eventId}/modules`
- `GET/POST /v1/events/{eventId}/admin/secret-santa/*`
- `GET/POST/PUT/DELETE /v1/events/{eventId}/admin/categories*`

## Removed legacy surface

Estas rotas deixaram de fazer parte do contrato suportado:

- `/api/canhoes/admin/*`
- `/v1/events/{eventId}/admin/nominees`
- `/v1/events/{eventId}/admin/nominees/{nomineeId}/*`
- `/v1/events/{eventId}/admin/nominations` não paginado
- `/v1/events/{eventId}/admin/votes`
- `/v1/events/{eventId}/admin/members`
- `/v1/events/{eventId}/admin/official-results`
- `/v1/events/{eventId}/admin/proposals`
- `/v1/events/{eventId}/admin/proposals/paged`

## Verification

```bash
dotnet build Canhoes.slnx
dotnet test Canhoes.Tests/Canhoes.Tests.csproj
```
