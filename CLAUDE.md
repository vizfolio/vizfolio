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
