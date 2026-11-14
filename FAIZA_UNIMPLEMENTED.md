# FAIZA GOLD — реестр незавершённого

Дата инвентаризации: 15 ноября 2026 г.

Этот файл — единственный реестр незавершённых, непроверенных и будущих работ.

| ID | Функция / идея | Источник | Уже есть | Чего не хватает | Статус | Приоритет | Критерий завершения |
|---|---|---|---|---|---|---|---|
| U-001 | Полный authenticated browser business-flow | `docs/FAIZA_V5_FINAL_IMPLEMENTATION_REPORT.md` | Login, dashboard и отдельные UI-сценарии проверены | Полный Playwright replay: смена, операции, transfer, settlement, representative, reversal, закрытие и assertions ролей | BLOCKED | P0 | Изолированная PostgreSQL БД, seed ролей и полный mutation replay |
| U-002 | RBAC browser replay | `docs/FAIZA_FINAL_READINESS.md` | API security suite проверяет роли и cashbox scope | Browser-проверка admin/accountant/cashier/manager/auditor | IMPLEMENTED_NOT_VERIFIED | P0 | Role matrix с положительными и отрицательными assertions |
| U-003 | Docker/TLS production smoke | `docker-compose.production.yml` | Compose, Caddy, health checks и production guards | Фактический запуск Docker Engine и TLS smoke | BLOCKED | Startup, migrations, health, HTTPS и restart проверены |
| U-004 | Backup/restore runtime | `scripts/backup-local.mjs` | Backup tooling и verification | Полный restore-прогон в отдельную тестовую БД | IMPLEMENTED_NOT_VERIFIED | Совпадают totals, audit и signed balances |
| U-005 | Accessibility/browser audit | `docs/FAIZA_V5_FINAL_IMPLEMENTATION_REPORT.md` | Responsive UI, keyboard focus, reduced motion | Полный axe/browser-аудит всех страниц | IMPLEMENTED_NOT_VERIFIED | Нет критических accessibility findings |
| U-006 | Новые функции за пределами ТЗ | Источник не подтверждён | Идейные упоминания в старых документах | Нет согласованного scope и acceptance criteria | OPTIONAL_FUTURE | Отдельное согласованное изменение ТЗ |

Филиалы, смены, transfers, settlements, partial payments, representatives, dashboard, CSV/PDF, audit, idempotency и ledger integrity реализованы в коде и подтверждены автоматическими suites. Непройденный browser или Docker запуск не превращает их в `NOT_IMPLEMENTED`.

Старые отчёты сохраняются как исторические доказательства и не являются текущим реестром. Актуальные требования находятся в `FAIZA_MASTER_TZ.md`.
