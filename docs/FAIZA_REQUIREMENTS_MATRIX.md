# Матрица требований FAIZA GOLD 2.0

Дата проверки: 2026-11-14. Статусы основаны на текущем исходном коде и результатах локальных тестов.

| ID | Требование | Статус | Файлы | Доказательства | Необходимое действие | Приоритет |
|---|---|---|---|---|---|---|
| FG-001 | Авторизация и роли Administrator/Accountant/Cashier/Manager/Auditor | IMPLEMENTED | `backend/src/FaizaCash.Api/Controllers/ApiEndpoints.cs`, `IdentityServices.cs` | `dotnet test`: 24/24 | Сохранить регрессию | P0 |
| FG-002 | Изоляция доступа к кассам | IMPLEMENTED | `CashboxAccessService.cs`, `AccessMiddleware.cs` | `ICashboxAccess.Require` вызывается в операциях и отчётах | Добавить интеграционный сценарий с двумя кассами | P0 |
| FG-003 | Несколько касс и лимит остатка | IMPLEMENTED | `Models.cs`, `ApiEndpoints.cs`, `CashDb.cs` | Cashbox и `BalanceLimit` реализованы | Покрыть API-тестом | P0 |
| FG-004 | Жизненный цикл кассовой смены | IMPLEMENTED | `Models.cs`, `ApiEndpoints.cs`, `LedgerService.cs`, migration `ShiftLifecycle` | 24 unit-теста включают shift rules | Проверить PostgreSQL concurrency | P0 |
| FG-005 | Неизменяемый ledger, idempotency и сторно | IMPLEMENTED | `LedgerService.cs`, `ReversalRules.cs`, `Models.cs` | `RequestKey`, posting sequence, reversal rules | Добавить интеграционный тест повторной публикации | P0 |
| FG-006 | Филиалы и привязка касс к филиалам | IMPLEMENTED | `Models.cs`, `CashDb.cs`, `ApiEndpoints.cs`, migration `Branches` | Branch, seeded Душанбе/Худжанд/Бохтар, FK Cashbox, branch filter | Добавить UI управления филиалами и расширенные branch-права | P0 |
| FG-007 | Контрагенты и поиск по контрагенту | PARTIAL | `Models.cs`, `DirectoryEndpoints.cs`, `ApiEndpoints.cs` | Counterparty и связь операции есть | Добавить основание платежа и расчёт задолженности | P1 |
| FG-008 | Частичные оплаты и переплата | IMPLEMENTED | `Models.cs`, `CashDb.cs`, `ApiEndpoints.cs`, migration `SettlementsAndPayments` | Settlement, PaymentAllocation, partial/paid status, overpayment and duplicate protection, history endpoint, integration scenario 2+3 | Add UI and cross-branch settlement visibility rules | P1 |
| FG-009 | Связь с внешним заказом/доставкой | IMPLEMENTED (reference model) | `Models.cs`, `Contracts.cs`, `ApiEndpoints.cs` | BasisType order/delivery/return/manual and BasisReference are required for settlements | Connect to external order/delivery systems when approved | P1 |
| FG-010 | Межкассовое перемещение без фиктивного дохода | IMPLEMENTED | `Models.cs`, `CashDb.cs`, `ApiEndpoints.cs`, migrations `CashTransfers`, `TransferOperationLinksNullable` | Serializable transaction, source/target locks, paired posted operations, idempotent RequestKey, 29 integration checks | Add dedicated transfer UI and concurrent transfer stress case | P0 |
| FG-011 | Отчётность и PDF/CSV | PARTIAL | `ReportEndpoints.cs`, `PdfReport.cs` | Cashbook, CSV, PDF, analytics реализованы | Добавить сводку по филиалам после FG-006 | P1 |
| FG-012 | Аудит действий | IMPLEMENTED | `AuditEndpoints.cs`, `JsonFileLogger.cs` | Audit event пишется в ключевых endpoints | Расширить на transfer/payment | P0 |
| FG-013 | Backup/restore | IMPLEMENTED | `scripts/backup-local.mjs`, `scripts/verify-backup.mjs`, `docs/backup.md` | Скрипты и инструкция существуют | Выполнить только при доступной тестовой БД | P0 |
| FG-014 | UI, адаптивность, reduced motion | IMPLEMENTED | `frontend/src/styles/*.css`, `frontend/src/pages` | 12/12 frontend-тестов, build успешен | Проверить браузерным E2E при доступном браузере | P2 |

Источники и допущения: официальный сайт компании — https://faiza.tj/; бизнес-сведения используются только как контекст исследования. Филиалы, платежные основания и интеграционный контракт являются учебной моделью дипломного проекта и не утверждают внутренние правила ООО «Фаиза».
# Дополнение V5.0 UI (2026-11-15)

| Область | Реализация | Проверка |
|---|---|---|
| Dashboard V5 | Реальный `/api/dashboard`, KPI, остатки по филиалам, открытые смены, последние операции | frontend build, API-backed component |
| Филиалы | Список, создание, редактирование статуса/города, отображение привязанных касс | backend endpoint + UI |
| Переводы | Источник/цель/сумма/основание, requestKey, блокировка повторной отправки, подтверждение ответа backend | UI build; endpoint already atomic |
| Задолженности | Список, поиск, основание, суммы, остаток, история платежей | UI build; overpayment enforced by backend |
| UX | Skeleton, hover, toast/status, dark/light theme, reduced-motion, responsive grids | frontend tests/build |
