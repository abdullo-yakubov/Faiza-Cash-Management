# Архитектура дипломной системы

Приложение разделено на Domain, Application, Infrastructure и Api; frontend построен на React/Vite. PostgreSQL используется через EF Core. Финансовая запись проходит путь `draft -> posted`; публикация выполняется сервисом ledger в транзакции, а исправление проведённой записи выполняется сторно.

Ключевые сущности: `User`, `Role`, `Cashbox`, `CashShift`, `CashOperation`, `Counterparty`, `Category`, `AuditEvent`. Доступ к кассам проверяется `ICashboxAccess` до чтения и изменения.

Архитектурные ограничения: не использовать floating point для денег, не изменять проведённый ledger, не создавать фиктивную интеграцию с реальными системами компании. Подробные подтверждения: `docs/architecture.md`, `backend/src/FaizaCash.Infrastructure/Persistence/CashDb.cs`.
