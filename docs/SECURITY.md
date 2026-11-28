# Security

The API uses JWT and role-aware access checks. Local credentials are generated into `.runtime/local.json`; keep that file outside Git and use strong independent secrets. Backups contain financial data. Only restore into a disposable test database during verification.

Review idempotency, cashbox authorization, database transaction boundaries, audit logging, and secret rotation before production. Dependency vulnerability data could not be fetched in this sandbox (`NU1900`), so package security status remains unverified.
