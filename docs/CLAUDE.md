# CLAUDE.md — FAIZA GOLD (FaizaCash)

Дипломный проект: система кассового учёта ООО «Фаиза». Требования (целевое состояние) — [docs/ТЗ_ГОСТ_34.602-2020.md](ТЗ_ГОСТ_34.602-2020.md); описание базовой версии — [docs/ТЗ.md](ТЗ.md). Тесты — [docs/TESTING.md](TESTING.md), решения — [docs/adr/](adr/README.md). Общаться с пользователем на русском языке.

## Стек
- Backend: ASP.NET Core 10 Minimal API, EF Core 10 + Npgsql, PostgreSQL 18, QuestPDF, xUnit.
- Frontend: React 19 + Vite 7, чистый JavaScript (без TypeScript), Recharts, lucide-react, hash-маршрутизация.
- Инфраструктура: Docker Compose (демо и production), Caddy, GitHub Actions.

## Структура
```
backend/src/FaizaCash.Domain          сущности (Entities/Models.cs), Money
backend/src/FaizaCash.Application     DTO, интерфейсы, BalanceRules/ShiftRules/ReversalRules
backend/src/FaizaCash.Infrastructure  CashDb, Migrations, LedgerService, BackupService, PdfReport, Identity
backend/src/FaizaCash.Api             Program.cs, Controllers/*Endpoints.cs, Middleware
backend/src/FaizaCash.Migrator        применение миграций в production
backend/tests/FaizaCash.UnitTests     xUnit
frontend/src                          app/App.jsx, pages, components, services/api.js, utils/route.js, styles
tests/                                API-наборы на PostgreSQL, integration/p0-p2, e2e.mjs (Playwright)
scripts/                              configure-local, run-local, seed-demo, backup
deploy/                               Caddyfile, SQL роли runtime
.runtime/                             локальные логи, дампы, секреты — НЕ код, не читать и не коммитить
```

## Команды (из корня `faiza-cash/`)
- Сборка: `dotnet build backend/FaizaCash.sln`
- Unit-тесты: `dotnet test backend/FaizaCash.sln`
- Frontend: `npm --prefix frontend ci`, `npm --prefix frontend test`, `npm --prefix frontend run build`
- Локальный запуск: `node scripts/run-local.mjs` (API на :5080) + `npm --prefix frontend run dev`
- Docker: `docker compose up --build -d` (frontend :8085, API :5085)
- Все проверки: `node tests/run-all.mjs` (наборы пересоздают схему `_test` БД, `restore.mjs` требует Docker)
- Новая миграция: `dotnet ef migrations add <Name> -p backend/src/FaizaCash.Infrastructure -s backend/src/FaizaCash.Api`

## Инварианты, которые нельзя нарушать
1. **Проведённые документы и `AuditEvents` неизменяемы** — это обеспечивают триггеры БД (`ImmutableLedger`). Исправление — только через сторно. Не обходить триггеры и не удалять их.
2. **Остаток не может стать отрицательным ни на одну дату и не может превысить `BalanceLimit`.** Проверки — в `BalanceRules`/`ShiftRules`, проведение — только через `LedgerService.Post` (Serializable + `FOR UPDATE`).
3. **Проведение возможно только в открытую смену** на дату документа. У кассы не больше одной открытой смены.
4. **Деньги — `decimal`, 2 знака, `Money.IsValid`.** В JSON суммы передаются строкой (`DecimalStringConverter`). Во frontend не использовать float-арифметику для итогов.
5. **Доступ к кассе проверяется на сервере**: `AccessMiddleware` + `ICashboxAccess.Require(...)` внутри транзакции мутирующих обработчиков. Admin видит всё, остальные — только назначенные кассы. Новый endpoint с `cashboxId` обязан вызывать `access.Require`.
6. **Каждое значимое изменение пишется в аудит** (`Audit.Add`).
7. Мутации с `RequestKey` должны быть идемпотентны, черновики — проверять `Version`.
8. Даты «сегодня» считаются в `Asia/Dushanbe` (`Local.Today`), не в UTC.
9. Миграции не редактировать задним числом — только добавлять новые.

## Соглашения
- Ошибки API — `Results.Problem(...)` / `BusinessException` с понятным текстом **на русском**, коды: 400 данные, 403 доступ, 404, 409 бизнес-конфликт.
- Роли: `admin`, `accountant`, `cashier`, `manager`, `auditor` — проверка через `[Authorize(Roles=...)]` у endpoint-а. Матрица прав — приложение А ТЗ по ГОСТ (admin не выполняет финансовых операций, ADR-0007).
- Интерфейс полностью на русском; цвета и отступы — через `styles/tokens.css`.
- Новые маршруты frontend добавлять в `pages` в `frontend/src/utils/route.js`.
- Тесты: бизнес-правило → unit в `backend/tests`; сценарий API → соответствующий набор в `tests/`.

## Безопасность
- Не читать и не выводить `.env`, `.runtime/local.json`, `.runtime/demo-users.json`, дампы `*.dump`.
- Интеграционные тесты запускать только на БД с суффиксом `_test`, никогда на демо-базе.
- Не ослаблять production-проверки в `Program.cs` (Secure cookie, ключ шифрования копий, KnownProxies, pending migrations).

## Известные хвосты
- `ApiEndpoints.cs` очень большой — при правках искать обработчик по маршруту (`MapPost("/api/...`).
- В `backend/src/FaizaCash.Api/` лежит лишний `*.tmp`.
