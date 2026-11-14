# Implementation Plan: Faiza Gold V5.0

## Overview

Modernize the existing Faiza Cash modular monolith into a demonstrable multi-branch cash-management system while preserving working behavior and proving every financial invariant with executable tests.

## Architecture Decisions

- Keep the current C# / ASP.NET Core / EF Core / PostgreSQL modular monolith; the project does not yet justify microservices.
- Treat the ledger as append-only after posting; corrections use reversals.
- Enforce branch/cashbox access in query and command services, with middleware as defense in depth.
- Ship only capabilities that are implemented and verified; documentation must distinguish implemented, partially implemented, and deferred items.

## Task List

### Phase 1: Baseline and integrity
- [ ] Task 1: Reproduce the current audit findings and establish build/test baseline.
- [ ] Task 2: Complete ledger regression cases and include all suites in the release runner.
- [ ] Task 3: Close critical API and database integrity gaps (authorization paths, optimistic posting, constraints, idempotency).

### Checkpoint: Foundation
- [ ] Backend build succeeds.
- [ ] Unit, integration, security, catalog, ledger, and frontend suites report real results.

### Phase 2: Financial workflow
- [ ] Task 4: Complete cash-shift open/close workflow and enforce posted-operation rules.
- [ ] Task 5: Implement atomic inter-cashbox transfers and access/audit coverage.
- [ ] Task 6: Improve reports, pagination, historical reproducibility, and dashboard data contracts.

### Phase 3: Product and diploma readiness
- [ ] Task 7: Align frontend navigation, branch/cashbox context, responsive states, and reduced-motion behavior.
- [ ] Task 8: Produce V5 reports, changelog, user/admin guides, and defense scenario from verified evidence.

### Checkpoint: Complete
- [ ] No unverified PASS claims remain.
- [ ] Remaining limitations are documented with reproduction steps.
- [ ] Local demonstration path is documented and repeatable.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Existing uncommitted work overlaps the audit | High | Preserve it; inspect diffs before editing shared files. |
| PostgreSQL/Docker unavailable | High | Run build/unit tests locally and mark DB/browser checks as blocked rather than faking PASS. |
| Large scope exceeds one iteration | Medium | Deliver vertical, verified slices and keep a living readiness report. |

## Open Questions

- Whether historical PDF snapshots are legally required or only a diploma demonstration concern.
- Whether production migration execution is handled by the dedicated migrator in deployment.
