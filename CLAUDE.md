# Project

This is a dotnet project for Vizfolio, an open source investment tracker

# Coding guidelines

1. Functionality should be unit tested with an aim of >80% code coverage on critical paths
2. Unit tests should be descriptive and document the Functionality
3. Code should be written for humans: modular, reusable, and self documenting, and easy to maintain
4. See `docs/er-diagram.md` for the data model. Changes should be updated here.
5. Database changes should be database agnostic unless a specific database implementation is explicitly requested. That means, use EF core and raise red flags if you can't to achieve a goal.

# Further documentation:

- **Fund Data**: Read `@docs/fund-data.md` when dealing with fund data
- **Security Data**: Read `@docs/security-data.md` when dealing with security data
- **Performance API**: Read `@docs/performance-api.md` when touching the performance endpoints, calculator strategies, or QFX ingestion of contributions/snapshots. Keep this document up to date when changes are made.
- **Frontend**: Read `@docs/frontend.md` when working on the Angular UI in `frontend/`. The .NET solution lives in `backend/` (run `dotnet` from there). All API routes are served under the `/api` prefix. Use the Angular CLI MCP server (`angular-cli` in `.mcp.json`) — call `get_best_practices` before writing Angular code so it follows current standalone/signals/zoneless patterns.
