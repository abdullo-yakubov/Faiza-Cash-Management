# FAIZA GOLD — единое техническое задание

Версия: 5.0 consolidated, 15 ноября 2026 г.

## 1. Назначение и границы

FAIZA GOLD — учебно-дипломная система регистрации и учёта кассовых операций ООО «Фаиза». Система охватывает филиалы, кассы, смены, приход/расход, transfers, контрагентов, задолженности, partial payments, representatives, dashboard, отчётность, audit и backup tooling. Она не подключается к подтверждённым production-данным и не моделирует неподтверждённые внутренние регламенты.

## 2. Фактический стек

- Backend: ASP.NET Core на .NET 10, C#.
- Frontend: React 19, Vite 7, Recharts, Lucide React.
- Database: PostgreSQL; ORM: Entity Framework Core.
- Authentication: JWT access token, refresh cookie rotation, CSRF/origin checks.
- Testing: .NET unit tests, Node integration/security/catalog/ledger suites, frontend Node tests.
- Deployment: Docker Compose, отдельный migrator и Caddy reverse proxy/TLS.

## 3. Архитектура и финансовые правила

Решение разделено на Domain, Application, Infrastructure, API и React UI. Проведённые операции неизменяемы; корректировка выполняется сторно. Ledger использует точные decimal-значения, posting sequence, idempotency keys, транзакции, блокировки и запрет отрицательного остатка. Доступ к данным дополнительно проверяется cashbox access service.

## 4. Роли

| Роль | Фактическая ответственность |
|---|---|
| Admin | Пользователи, роли, доступы, каталоги, филиалы и разрешённые операции |
| Accountant | Каталоги, отчётность, операции, settlements и доступные кассы |
| Cashier | Кассовая работа, смены и операции назначенных касс |
| Manager | Разрешённые бизнес-сценарии и settlements доступных касс |
| Auditor | Чтение доступных данных и audit без изменения финансовых записей |

## 5. Реализованные функции

| Область | Реализация | Подтверждение |
|---|---|---|
| Branches/cashboxes | Branch entity, FK, scope и фильтры | integration/security |
| Shifts | Open/close/history и rules | unit/API; browser replay pending |
| Income/expense | Draft → post, validation, ledger и limits | integration/ledger |
| Transfers | Atomic paired postings, locking, idempotency | integration |
| Counterparties | Catalog, normalization, search и связи | catalog/integration |
| Settlements | Basis, partial allocation, history, overpayment guard | integration |
| Representatives | collected → handed_over → accepted | API/UI; full browser replay pending |
| Dashboard | Real API KPI, balances, cashflow, debts, filters | frontend build/tests |
| Reports | Cashbook, CSV, PDF, running balance и limits | integration |
| Audit/backup | Audit events, backup и verification scripts | security/integration; restore pending |

## 6. Интерфейс

Страницы включают login, overview, operations, users, audit, reports, dashboard, branches, shifts, transfers, debts и representatives. UI поддерживает responsive layout, loading/error/success states, keyboard focus, focus trap, contrast tokens, hover transitions и `prefers-reduced-motion`. Полный browser accessibility audit зарегистрирован как U-005.

## 7. Database

Ключевые сущности: Organization, User, Branch, Cashbox, CashShift, CashOperation, Category, Counterparty, Settlement, PaymentAllocation, CashTransfer, RepresentativeReceipt и AuditEvent. Миграции находятся в `backend/src/FaizaCash.Infrastructure/Migrations`; их удаление запрещено. Финансовые ограничения enforced в application/database слоях.

## 8. Безопасность и API

Ключевые группы endpoint-ов: `/api/auth`, `/api/users`, `/api/cashboxes`, `/api/branches`, `/api/shifts`, `/api/operations`, `/api/transfers`, `/api/settlements`, `/api/representative-receipts`, `/api/dashboard`, `/api/reports`, `/api/categories`, `/api/counterparties`, `/api/audit` и `/health`. Mutation endpoints требуют authorization, validation, audit и idempotency там, где повторная публикация возможна.

## 9. Тестирование и приёмка

Текущее воспроизводимое состояние: backend build PASS, unit 24/24, frontend 12/12, integration 30/30, security 16/16, catalog 92/92, ledger 7/7. Полный browser mutation E2E, Docker/TLS smoke и restore runtime не считаются PASS без запуска и находятся в `FAIZA_UNIMPLEMENTED.md`.

## 10. Установка и ограничения

Локальный запуск и миграции описаны в `README.md`, `docs/deployment.md` и `scripts/run-local.mjs`. Production требует внешних secrets, PostgreSQL, trusted proxy, HTTPS и `RefreshCookie:Secure=true`; отключать authorization или TLS запрещено.

Единственный актуальный список незавершённого — `FAIZA_UNIMPLEMENTED.md`. Новые идеи не добавляются в это ТЗ без подтверждённого источника и согласования scope.
