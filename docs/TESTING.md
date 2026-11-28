# Testing

`dotnet build backend/FaizaCash.sln` builds the backend. `dotnet test backend/tests/FaizaCash.UnitTests/FaizaCash.UnitTests.csproj` runs pure unit tests. `npm --prefix frontend ci`, `npm --prefix frontend run build`, and `npm --prefix frontend test` verify the frontend.

`node tests/run-all.mjs` includes database/API checks and may recreate the designated test schema; use only a disposable database. In the initial audit, the backend build and 27 xUnit tests passed, and the frontend build and 12 Node tests passed. Database integration, backup restore, browser, and concurrent ledger checks were not run.
