# Setup

Follow the root README for Windows and Docker setup. Required tools: .NET SDK 10.0.203, Node.js 22, and PostgreSQL 18 for the local scripts. `node scripts/configure-local.mjs` creates development and test databases and writes `.runtime/local.json` with local credentials. Do not commit this file or reuse a working database for tests. Run `node scripts/run-local.mjs` for the API and `npm --prefix frontend run dev` for the UI. Docker uses a local `.env` based on `.env.example`.

The test runner can recreate the `_test` schema. Confirm the target connection before invoking it.
