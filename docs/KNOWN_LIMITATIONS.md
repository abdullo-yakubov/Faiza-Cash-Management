# Known limitations

- Isolated PostgreSQL backup restore has not been verified; diploma evidence for recovery is incomplete.
- API/database integration, concurrent debits and transfers, and full browser journeys were not run in this audit.
- NuGet vulnerability feed returned `NU1900` in the sandbox.
- Real app screenshots and deployment health have not been captured.
- Passed unit and frontend tests do not prove all financial invariants. No production-ready claim is made.
