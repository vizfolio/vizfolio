# Frontend

The Vizfolio UI is an Angular single-page app that lives in `web/` and talks to the .NET API over REST. It is intentionally **self-contained**: it has its own `package.json`, `angular.json`, and `web/.gitignore`, holds no references into the .NET projects, and communicates only over HTTP. That keeps it easy to extract into its own repository later without touching the backend.

## Stack

- **Angular** (standalone components, signals, zoneless change detection — `OnPush` is the default). Scaffolded with `ng new … --zoneless --style scss --ssr=false`.
- **Angular CDK** (`@angular/cdk`) for accessible headless primitives (overlay, a11y, drag-drop, `CdkTable`, virtual scroll). We deliberately did **not** pull in Angular Material — styling stays custom. Material can be layered on later if a full design system is wanted.
- **Node 22** + npm. The Angular CLI is pinned as a dev dependency in `web/package.json` and run via `npm`/`npx` (not packaged by Nix), so the frontend toolchain travels with the workspace.

## Layout

```
web/
├── angular.json          # serve builder wires proxy.conf.json
├── package.json          # Angular + @angular/cdk deps, ng scripts
├── proxy.conf.json       # dev proxy: /api → http://localhost:5261
└── src/app/              # application shell (feature code added incrementally)
```

## Dev loop

The Nix flake (`flake.nix`) provides both toolchains. Enter it with `nix develop` (or `direnv allow` if you use direnv), then run the two processes side by side:

```bash
# terminal 1 — API on http://localhost:5261
dotnet run --project src/Vizfolio.Api

# terminal 2 — SPA on http://localhost:4200
npm --prefix web start        # == ng serve
```

`ng serve` proxies every `/api/*` request to the API (`web/proxy.conf.json`), so the browser sees a single origin and **no CORS configuration is needed** in development. Open `http://localhost:4200`.

## API contract

All API routes are served under a global **`/api`** prefix (set in `src/Vizfolio.Api/Program.cs` via `app.UseFastEndpoints(c => c.Endpoints.RoutePrefix = "api")`). For example the health check is `GET /api/health` and portfolio performance is `GET /api/portfolios/{id}/performance`. Swagger UI remains at `/swagger` and the OpenAPI document at `/swagger/v1/swagger.json` (these are not affected by the route prefix).

## Angular CLI MCP server

The workspace ships an `angular-cli` MCP server config in the repo-root `.mcp.json`, run via `npx -y @angular/cli mcp`. It grounds AI-assisted development in current Angular guidance rather than stale training data. **Before writing or modifying Angular code, call its `get_best_practices` tool** (and `search_documentation` / `find_examples` as needed) so generated code uses standalone components, signals, `inject()`, and native control flow (`@if`/`@for`). In Claude Code, confirm it is connected with `/mcp` (you should see the `angular-cli` server and its tools).

## Deferred: hosting the SPA inside the .NET app

Today the frontend is served independently (`ng serve`) and the .NET app is API-only — the cleanest arrangement while the UI is young and most likely to move to its own repo. When co-hosting is wanted, it is a small additive change on the backend:

1. `npm --prefix web run build` emits static assets to `web/dist/vizfolio-web/`.
2. In `Program.cs`, add `app.UseStaticFiles()` (pointed at the built assets) and `app.MapFallbackToFile("index.html")` so client-side routes resolve to the SPA. Gate it behind config so it is off by default.
3. Optionally add an MSBuild step to run `ng build` during `dotnet publish`.

Because the API already lives under `/api`, the SPA fallback at `/` will not collide with it.

## Deferred: typed API client

To keep the SPA in lockstep with the backend contract, generate a TypeScript client from the FastEndpoints OpenAPI document (`/swagger/v1/swagger.json`). This preserves the decoupling (the frontend depends on the published contract, not on .NET types) and stays valid after an extraction to a separate repo.
