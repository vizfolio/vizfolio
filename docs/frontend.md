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
        ├── shared/ui/    # reusable presentational components (stat-card, performance-headline, perf-chart, returns-chart)
        └── features/     # routed pages: dashboard, coming-soon (placeholders)
```

## Application shell

The UI is a **shell + features** structure so sections can be added without touching the chrome:

- **`layout/shell`** — CSS-grid frame: fixed top bar, left sidebar, scrollable `<router-outlet>` main area. Owns the off-canvas sidebar state used on narrow (< 768px) screens.
- **`layout/sidebar`** — primary nav rendered from the `NavItem[]` array in `layout/nav-items.ts`. **Add a nav link by adding one entry there.** Uses `routerLink` + `routerLinkActive`.
- **`layout/topbar`** — brand, hamburger (narrow screens, emits `menuToggle`), theme toggle, and a disabled account-menu placeholder for when auth lands.
- **`shared/ui`** — presentational, `input()`-driven components with no data dependencies: `stat-card` (label/value/trend/incomplete, plus an optional `hint` explaining the figure and `featured` for the page's headline), `perf-chart`, `returns-chart`, and reusable form controls that wrap native inputs behind app design tokens (`date-field`, `select-field`, `file-upload`) so styling/behaviour live in one place and swap without touching call sites.
- **`features/*`** — lazy-loaded routed pages. `dashboard` is the landing page; `coming-soon` is a shared placeholder whose heading is bound from route `data.title` via `withComponentInputBinding()`.

## Theming

Theming is driven entirely by CSS custom properties in `src/styles.scss`, split into two layers:

- **Raw tokens** — the fixed brand palette (`--brand-*`, gradients). Never change per theme.
- **Semantic tokens** — what components actually consume: `--color-bg`, `--color-surface`, `--color-surface-2`, `--color-text`, `--color-text-muted`, `--color-border`, `--color-primary`, `--color-accent`, `--color-positive`, `--color-negative`, plus `--radius-*`, `--space-*`, and shadows.

Components reference **only** semantic tokens, so re-theming never touches component SCSS. Light is the default on `:root`; dark overrides live under `:root[data-theme='dark']`. **Adding a theme = one new `:root[data-theme='name'] { … }` block** plus widening the `ThemeName` union. `ThemeService` (`core/theme`) holds the active theme in a signal, reflects it onto `<html data-theme>` via an `effect`, persists it to `localStorage`, and seeds the initial value from storage → `prefers-color-scheme`.

## Charts

`shared/ui/perf-chart` is a thin wrapper around **Chart.js** (`chart.js`, MIT-licensed) — the only place Chart.js is imported, so the library is swappable from one file. It takes `labels` + typed `PerfDataset[]` inputs, resolves series colors from the semantic theme tokens (so charts follow light/dark), and rebuilds when inputs or the theme change. The "Value over time" charts (dashboard, portfolio Performance, account Performance) plot the performance response's `series` — real balances valued from price history, with each interval's deposits/withdrawals as bars — mapped onto chart arrays by `buildValueSeries` (`shared/util/performance-format.ts`), which also formats the axis labels to suit the API-chosen interval. A `null` value (a date where some holding couldn't be valued) is a gap in the line, so `PerfDataset.data` is `(number | null)[]`. `perf-chart` takes an optional `valueFormat` (`number` | `currency` | `percent`, plus `currencyCode`) that formats y-axis ticks and tooltips; `percent` expects values already in percent and emphasizes the zero line.

`shared/ui/returns-chart` is the "Investment returns over time" card, shown under the value chart on the dashboard and in `performance-summary` (portfolio and account Performance). A `%` / currency toggle (`aria-pressed` buttons) switches between the series' `cumulativeReturn` (ends at the time-weighted "Investment return", not the money-weighted "Your return" headline — a caption says so, and flags a Modified Dietz fallback as approximate) and `investmentGain` (value − starting balance − net contributions), mapped by `buildReturnSeries`. `headingLevel` (2 or 3) fits the host page's outline. The dashboard's offline `SAMPLE_PERFORMANCE` carries a sample series, including consistent return/gain values.

## Returns: "Your return" and "Investment return"

The headline return is the **money-weighted** `returns.moneyWeighted` (XIRR), labelled **"Your return"** (a personal rate of return). The time-weighted `returns.timeWeighted` is second, labelled **"Investment return"**. Both appear in the shared headline row (Your return `featured`), each with a hint explaining it and a detail line from `returnDetail` (`performance-format.ts`): "a year", "over the period", the annualized equivalent ("+10.2% a year"), "approximate" on a fallback, or the reason a figure is unknown. Labels, hints and the plain-language text for backend reason/cause codes live in `shared/util/reason-text.ts` (`returnReasonText`, `fallbackText`, `returnMethodText`, `causeText`); add new codes there. The headline row is one shared component, `shared/ui/performance-headline`, used by the dashboard and by `performance-summary` (portfolio and account Performance), so all three pages show the same four cards in the same order, each figure once: **Balance** (ending value, "from $X at the start", and a completeness badge projected into the card via `statBadge`, describing whichever end of the period is worse off; `balanceLabel="Portfolio value"` on the dashboard) · **Your return** (featured) · **Investment return** · **Net contributions** (with "$X in · $Y out" underneath unless `showGrossFlows` is false — it's off on the portfolio-scope dashboard and Performance page, where transfers between accounts inflate the gross figures). `performance-summary` adds a native `<details>` "How this is calculated" panel (closed by default) with the methods, annualized rate, fallback and reason text. Screens stay provider-agnostic: no broker is named in return labels, hints or notes. See [performance-api.md](./performance-api.md#returns).

## Data flow

`core/api/portfolio-api.service.ts` (`@Service`, MIT-clean) wraps `HttpClient` against the `/api` prefix and returns typed Observables; DTO interfaces in `core/api/models/` mirror `backend/src/Vizfolio.Api/Endpoints/Portfolios/PerformanceResponses.cs`. Feature components adapt those Observables to signals at the edge. When the DB has no portfolios (or the API is unreachable) the dashboard degrades to clearly-labelled sample data so the shell stays legible.

## Account detail tabs

`features/accounts/detail/account.ts` is a tabbed container over the account-scoped endpoints. Each tab is its own small component taking `portfolioId`/`accountId` inputs and following the same shape: a `status` signal + `toObservable(query) → switchMap(api) → subscribe` feeding a data signal.

- **Holdings** (`account-holdings.ts`) and **Ledger** (`account-ledger.ts`) render the reusable `shared/ui/data-table` (client-side sortable, fully presentational). Their row DTOs (`core/api/models/holdings.models.ts`, `ledger.models.ts`) are `type` aliases — not `interface`s — so they satisfy `DataTable`'s `Record<string, unknown>` row constraint. Money/quantity cells format via `shared/util/performance-format.ts` (`formatMoney`, `formatQuantity`). Holdings are valued by the same engine as performance (see the Performance API's "Holdings & ledger endpoints"): each row has a `status` (`Valued` / `NotHeld` / `Missing`), a `valuationSource` and a "Priced as of" date; the account's cash (incl. its settlement fund) is the last row, `kind: "Cash"`. Missing rows are listed by cause in a "couldn't be valued" note. The "Adjust starting positions" form (`opening-balance-form.ts`) prefills from `getOpeningPositions` — positions held before the imported history are filled in to confirm, ones that can't be derived are blank, cash is a `$CASH` row. The ledger reuses the `date-field` control for its optional trade-date filter.

## Imports: one drop zone, summaries, warnings and undo

- **One drop zone** (`features/accounts/import-drop-zone/`), on the Accounts page and — for a portfolio with no
  accounts yet — on the dashboard next to `import-onboarding` (export steps per broker; broker names are fine there,
  they're import formats). It takes several files (`file-upload` with `multiple`) and imports them **one at a time**
  through the portfolio endpoint, which finds each file's account by number or, for a file without one, by the
  transactions already in an account. When the server answers `NeedsAccountSelection`, the file waits for an inline
  **`account-picker`** (candidates with their evidence — "2,514 matching transactions · 3 shared funds" — the
  likeliest pre-selected, or a new account; a new account for a file without a number asks for the institution and
  number) and the queue pauses, since the answer can affect later files; the answer is sent with the file again.
  The Format override sits under "Advanced". The manual add-account form is a collapsed "Add an account manually"
  card.
- **Summary** (`import-summary/`), after every import here and on an account's Import tab: the detected format;
  each account and how it was found (`import-text.ts` → `routingNote`), the dates the file covers, added / already
  there / updated / failed counts with **failure reasons**; warnings; implied contributions in the account's
  currency; "Fetching prices…" until the account's value and "Your return" are in (polled); and **Undo this
  import**. A re-upload of the same file shows the earlier result (`AlreadyImported`).
- **Account Import tab**: a file without account details that clearly belongs to another account comes back
  `LikelyOtherAccount` and is held with **Import into {account}** / **Import here anyway**. A 422 is
  `AccountMismatch` (a QFX for other accounts).
- **Warnings** (`import-warnings/`): what the parser didn't fully understand, collapsed in a native `<details>`.
- **Undo** (`undo-import/undo-import-confirm.ts`): loads the server's preview, shows it in an inline confirmation
  (focus moves to its heading; Undo / Cancel) and undoes. Used by the summary and the **Import history**
  (`import-history/import-history-list.ts`: uploads newest first, filtered to the account on an account page, with
  a Download link for the stored file; undone imports stay listed, struck through).
- **Reprocess** lives on **Settings → Ledger** ("Reprocess imports").

## Data health

- **Account → Data health tab** (`detail/account-health.ts`, replaces the old History tab; `?tab=health` opens it):
  the account's findings via **`health-list/`** — "Needs attention" (Blocking) then "Worth knowing" (Info), each with
  its action: *Fetch prices* (queues `POST /api/prices/refresh`, then re-checks), *Add a price provider* (Settings),
  *Adjust starting positions* / *Import a file* (switch tab), *Review* implied contributions (a by-year table from
  `GET .../implied-contributions`). History coverage facts sit in a collapsed panel. It re-checks while prices
  download.
- **Accounts list**: each account's value, this year's "Your return" (account performance from Jan 1, fetched in
  parallel) and a health dot from one `GET .../health` call, linking to that account's Data health tab.
- **Readable reasons** (`shared/util/reason-text.ts`): `missingText` names what couldn't be valued and why ("We
  couldn't value XYZ at the end of the period: no price available (+2 more).") — in the completeness badge's tooltip
  and as a note under the headline cards (dashboard, Performance, account Performance) with a "Review data health"
  link. The Holdings tab lists missing holdings by cause.
- **Tabs**: Performance · Holdings · Ledger · Data health · Import · Adjust starting positions (the old "Opening
  balance" form, rarely needed). The Ledger shows the normalized type next to the broker's label.
- Global `.btn` styles live in `styles.scss`; new components use them instead of copying button CSS.

## Prices: Settings and "Updating…"

- **Settings → Prices** (`features/settings/price-settings/`) lists the price providers in the order they're tried
  with their state (Active / Needs an API key / Off; Stooq flagged as adjusted-only), a password field to save or
  remove an API key (hidden when the key comes from server configuration), the background refresh status with a
  "Fetch prices now" button (it polls the status while a fetch runs), and the price series that need a look (no data,
  failed, history starting late). With no provider set up it points to getting a free Tiingo key.
- **Pending prices.** Right after an import, missing values whose prices are still downloading come back with cause
  `PricesPending`. The completeness badge then reads "Updating…" (not "Estimate"), the Holdings tab says
  "Prices updating…", and the dashboard, Performance page, account Performance tab and Holdings tab re-fetch every
  5 s until the cause clears (`shared/util/poll.ts` → `pollWhilePending`, capped at 5 minutes).

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
