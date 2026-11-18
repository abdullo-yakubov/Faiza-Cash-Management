# FAIZA GOLD / Faiza Cash — Technical Specification V1.1

Дата аудита: 2026-11-15. Статус: **V1.1 PARTIAL / NOT RELEASE READY**.

## 1. Назначение

Faiza Cash — кассовая система для филиалов, касс, приходных и расходных операций, смен, межкассовых переводов, взаиморасчётов, платежей, отчётов и аудита. Работа выполнена поверх V1.0; миграции, рабочий код и финансовые данные не удалялись.

## 2. Стек и архитектура

- Backend: C#/.NET 10, ASP.NET Core Minimal API, EF Core 10, PostgreSQL/Npgsql, JWT/refresh tokens, QuestPDF, xUnit.
- Frontend: React 19, Vite 7, JSX, lucide-react, recharts, Node test runner, Nginx.
- Deployment: Docker Compose, Caddy, отдельный migrator, PostgreSQL.
- Backend layers: Domain, Application, Infrastructure, Api, Migrator; frontend — pages/components/services/hooks/styles/tests.

## 3. Финансовое ядро

Деньги используют decimal и PostgreSQL numeric(18,2). Модель содержит кассы, валюты, смены, операции, последовательности документов, переводы, расчёты, распределения платежей и представительские расписки. Операции поддерживают draft/posted, уникальные номера документов, RequestKey для идемпотентности, optimistic concurrency Version, ограничения суммы и запрет изменения проведённых документов. Аудит пишется в AuditEvent.

## 4. Статус функций

| ID | Функция | Статус | Доказательство |
|---|---|---|---|
| F-01 | Авторизация, роли, доступ к кассам | IMPLEMENTED_UNTESTED | Program.cs, Authorization, AccessMiddleware |
| F-02 | Кассы, филиалы, валюты, справочники | IMPLEMENTED_UNTESTED | CashDb.cs, DirectoryEndpoints.cs, migrations |
| F-03 | Черновики, проведение, сторнирование | IMPLEMENTED_UNTESTED | ApiEndpoints.cs, LedgerService.cs |
| F-04 | Смены и контроль остатка | VERIFIED | ShiftRulesTests, BalanceRulesTests; 27/27; неизвестные направления отклоняются |
| F-05 | Межкассовые переводы | IMPLEMENTED_UNTESTED | CashTransfer, LedgerService, migrations |
| F-06 | Задолженности и частичные платежи | IMPLEMENTED_UNTESTED | Settlement, PaymentAllocation |
| F-07 | Представительские расписки | IMPLEMENTED_UNTESTED | RepresentativeReceipt, migration |
| F-08 | CSV/PDF и кассовая книга | IMPLEMENTED_UNTESTED | ReportEndpoints.cs, PdfReport.cs |
| F-09 | Dashboard, адаптивная и тёмная тема | PARTIAL | V5Pages.jsx, styles |
| F-10 | Backup/restore | IMPLEMENTED_UNTESTED | BackupService.cs, ApiEndpoints.cs, BackupsPage.jsx, scripts/backup-local.mjs, verify-backup.mjs |
| F-11 | Audit trail | IMPLEMENTED_UNTESTED | CashDb.SaveChangesAsync, AuditEndpoints.cs |

## 5. История V1.0 → V1.1

V1.0 сохранена: backend/frontend, миграции, Docker, API-контракты, тесты и deployment-файлы оставлены. V1.1 фиксирует единое ТЗ, статусы, переводы, расчёты, платежи, расписки, backup/restore и критерии выпуска. Функции не названы VERIFIED без проверки.

## 6. Фактические проверки

| Проверка | Результат |
|---|---|
| dotnet build backend/FaizaCash.sln --no-restore | PASS, 0 warnings, 0 errors |
| dotnet test backend/FaizaCash.sln --no-restore | PASS, 24/24 |
| frontend: npm test | BLOCKED: отсутствует react; npm ci остановлен на EPERM при замене Rollup native binary |
| frontend: npm run build | BLOCKED: vite недоступен из-за неполной установки |
| node tests/run-all.mjs | NOT RUN: API/PostgreSQL/Docker не подняты |
| Browser responsive/UI checks | NOT RUN: browser environment unavailable |

## 7. Нереализованные или неподтверждённые требования

- Полный интеграционный прогон с отдельной тестовой PostgreSQL БД.
- Тестовый backup → restore в безопасной БД.
- Полный frontend build и render-тесты после восстановления node_modules.
- Browser-проверка Dashboard и responsive layouts.
- Подтверждение конкурентных сценариев в PostgreSQL.
- Backend API и UI управления резервными копиями ещё не добавлены; доступны локальные scripts для backup и verify.

## 8. Критерии готовности

V1.1 может стать release-ready только после успешных backend/frontend/integration проверок, backup/restore, проверки прав доступа и конкурентных финансовых сценариев. До этого статус: **V1.1 PARTIAL / NOT RELEASE READY**.

## 9. Запуск

```powershell
dotnet restore backend/FaizaCash.sln
dotnet build backend/FaizaCash.sln
dotnet test backend/FaizaCash.sln
cd frontend
npm ci
npm test
npm run build
cd ..
node tests/run-all.mjs
```

