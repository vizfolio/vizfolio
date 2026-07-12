# Frontend

The Vizfolio UI is an Angular single-page app that lives in `frontend/` and talks to the .NET API (in `backend/`) over REST. It is intentionally **self-contained**: it has its own `package.json`, `angular.json`, and `frontend/.gitignore`, holds no references into the .NET projects, and communicates only over HTTP. That keeps it easy to extract into its own repository later without touching the backend.

## Repo layout

```
backend/     # .NET solution (Vizfolio.slnx, global.json, dotnet-tools.json, src/, tests/)
frontend/    # Angular workspace (this doc)
docs/        # shared documentation
```

Run `dotnet` commands from `backend/` (that is where `global.json` pins the SDK). Run `ng`/`npm` from `frontend/` (or with `npm --prefix frontend …`).

## Stack

- **Angular** (standalone components, signals, zoneless change detection — `OnPush` is the default). Scaffolded with `ng new … --zoneless --style scss --ssr=false`.
- **Angular CDK** (`@angular/cdk`) for accessible headless primitives (overlay, a11y, drag-drop, `CdkTable`, virtual scroll). We deliberately did **not** pull in Angular Material — styling stays custom. Material can be layered on later if a full design system is wanted.
- **Node 22** + npm. The Angular CLI is pinned as a dev dependency in `frontend/package.json` and run via `npm`/`npx` (not packaged by Nix), so the frontend toolchain travels with the workspace.

## Frontend layout

```
frontend/
├── angular.json          # serve builder wires proxy.conf.json
├── package.json          # Angular + @angular/cdk + chart.js deps, ng scripts
├── proxy.conf.json       # dev proxy: /api → http://localhost:5261
└── src/
    ├── styles.scss       # global reset + theme design tokens (see Theming)
    └── app/
        ├── app.ts        # root component; renders <app-shell/>
        ├── app.config.ts # providers: zoneless, HttpClient, router (+ input binding)
        ├── app.routes.ts # lazy routes (dashboard is default)
        ├── core/         # cross-cutting singletons (no UI)
        │   ├── api/      # PortfolioApiService + typed DTO models
        │   └── theme/    # ThemeService (light/dark, persisted)
        ├── layout/       # app chrome: shell, sidebar, topbar, theme-toggle, nav-items
        ├── shared/ui/    # reusable presentational components (stat-card, perf-chart)
        └── features/     # routed pages: dashboard, coming-soon (placeholders)
```

## Application shell

The UI is a **shell + features** structure so sections can be added without touching the chrome:

- **`layout/shell`** — CSS-grid frame: fixed top bar, left sidebar, scrollable `<router-outlet>` main area. Owns the off-canvas sidebar state used on narrow (< 768px) screens.
- **`layout/sidebar`** — primary nav rendered from the `NavItem[]` array in `layout/nav-items.ts`. **Add a nav link by adding one entry there.** Uses `routerLink` + `routerLinkActive`.
- **`layout/topbar`** — brand, hamburger (narrow screens, emits `menuToggle`), theme toggle, and a disabled account-menu placeholder for when auth lands.
- **`shared/ui`** — presentational, `input()`-driven components with no data dependencies: `stat-card` (label/value/trend/incomplete), `perf-chart`, and reusable form controls that wrap native inputs behind app design tokens (`date-field`, `select-field`, `file-upload`) so styling/behaviour live in one place and swap without touching call sites.
- **`features/*`** — lazy-loaded routed pages. `dashboard` is the landing page; `coming-soon` is a shared placeholder whose heading is bound from route `data.title` via `withComponentInputBinding()`.

## Theming

Theming is driven entirely by CSS custom properties in `src/styles.scss`, split into two layers:

- **Raw tokens** — the fixed brand palette (`--brand-*`, gradients). Never change per theme.
- **Semantic tokens** — what components actually consume: `--color-bg`, `--color-surface`, `--color-surface-2`, `--color-text`, `--color-text-muted`, `--color-border`, `--color-primary`, `--color-accent`, `--color-positive`, `--color-negative`, plus `--radius-*`, `--space-*`, and shadows.

Components reference **only** semantic tokens, so re-theming never touches component SCSS. Light is the default on `:root`; dark overrides live under `:root[data-theme='dark']`. **Adding a theme = one new `:root[data-theme='name'] { … }` block** plus widening the `ThemeName` union. `ThemeService` (`core/theme`) holds the active theme in a signal, reflects it onto `<html data-theme>` via an `effect`, persists it to `localStorage`, and seeds the initial value from storage → `prefers-color-scheme`.

## Charts

`shared/ui/perf-chart` is a thin wrapper around **Chart.js** (`chart.js`, MIT-licensed) — the only place Chart.js is imported, so the library is swappable from one file. It takes `labels` + typed `PerfDataset[]` inputs, resolves series colors from the semantic theme tokens (so charts follow light/dark), and rebuilds when inputs or the theme change. Note: the performance API returns period *aggregates*, not a time series, so the dashboard chart currently renders a **synthetic** monthly curve (`features/dashboard/dashboard.util.ts`, marked `TODO(perf-timeseries)`) until a time-series endpoint exists.

## Data flow

`core/api/portfolio-api.service.ts` (`@Service`, MIT-clean) wraps `HttpClient` against the `/api` prefix and returns typed Observables; DTO interfaces in `core/api/models/` mirror `backend/src/Vizfolio.Api/Endpoints/Portfolios/PerformanceResponses.cs`. Feature components adapt those Observables to signals at the edge. When the DB has no portfolios (or the API is unreachable) the dashboard degrades to clearly-labelled sample data so the shell stays legible.

## Account detail tabs

`features/accounts/detail/account.ts` is a tabbed container over the account-scoped endpoints. Each tab is its own small component taking `portfolioId`/`accountId` inputs and following the same shape: a `status` signal + `toObservable(query) → switchMap(api) → subscribe` feeding a data signal.

- **Holdings** (`account-holdings.ts`) and **Ledger** (`account-ledger.ts`) render the reusable `shared/ui/data-table` (client-side sortable, fully presentational). Their row DTOs (`core/api/models/holdings.models.ts`, `ledger.models.ts`) are `type` aliases — not `interface`s — so they satisfy `DataTable`'s `Record<string, unknown>` row constraint. Money/quantity cells format via `shared/util/performance-format.ts` (`formatMoney`, `formatQuantity`). Holdings values come from the latest snapshot (see the Performance API's "Holdings & ledger endpoints"); a holding with no snapshot is left unvalued and surfaced in a "not valued" note. The ledger reuses the `date-field` control for its optional trade-date filter.

## Dev loop

The Nix flake (`flake.nix`) provides both toolchains. Enter it with `nix develop` (or `direnv allow` if you use direnv), then run the two processes side by side:

```bash
# terminal 1 — API on http://localhost:5261
cd backend && dotnet run --project src/Vizfolio.Api

# terminal 2 — SPA on http://localhost:4200
npm --prefix frontend start        # == ng serve
```

`ng serve` proxies every `/api/*` request to the API (`frontend/proxy.conf.json`), so the browser sees a single origin and **no CORS configuration is needed** in development. Open `http://localhost:4200`.

## API contract

All API routes are served under a global **`/api`** prefix (set in `backend/src/Vizfolio.Api/Program.cs` via `app.UseFastEndpoints(c => c.Endpoints.RoutePrefix = "api")`). For example the health check is `GET /api/health` and portfolio performance is `GET /api/portfolios/{id}/performance`. Swagger UI remains at `/swagger` and the OpenAPI document at `/swagger/v1/swagger.json` (these are not affected by the route prefix).

## Angular CLI MCP server

The repo-root `.mcp.json` registers an `angular-cli` MCP server. Because Claude Code launches MCP servers from the project root (there is no `cwd` field) and Angular's `list_projects` reads `angular.json`, the command wraps `npx -y @angular/cli mcp` in a `cd "$CLAUDE_PROJECT_DIR/frontend"` so the workspace is found. It grounds AI-assisted development in current Angular guidance rather than stale training data. **Before writing or modifying Angular code, call its `get_best_practices` tool** (and `search_documentation` / `find_examples` as needed) so generated code uses standalone components, signals, `inject()`, and native control flow (`@if`/`@for`). In Claude Code, confirm it is connected with `/mcp`.

## Deferred: hosting the SPA inside the backend image

Today the frontend is served independently (`ng serve`) and the .NET app is API-only — the cleanest arrangement while the UI is young and most likely to move to its own repo. The intended production path is a **multi-stage container build**: build the SPA, then copy its `dist` into the backend image so a single image serves both.

1. Stage 1 (`node:22`): `npm ci && npm run build` in `frontend/` → static assets in `frontend/dist/vizfolio-web/`.
2. Stage 2 (`dotnet/sdk:10` → `dotnet/aspnet:10`): `dotnet publish backend/…`, then `COPY --from=stage1 /frontend/dist/vizfolio-web ./wwwroot`.
3. In `Program.cs`, add `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")` so client-side routes resolve to the SPA. Gate it behind config so local `dotnet run` stays API-only.

Because the API already lives under `/api`, the SPA fallback at `/` will not collide with it.

## Deferred: typed API client

To keep the SPA in lockstep with the backend contract, generate a TypeScript client from the FastEndpoints OpenAPI document (`/swagger/v1/swagger.json`). This preserves the decoupling (the frontend depends on the published contract, not on .NET types) and stays valid after an extraction to a separate repo.
