# GAP-анализ текущей системы

## Подтверждённые gaps

| Приоритет | Вывод | Доказательство |
|---|---|---|
| P0 | Кассовые смены функционально не завершены: есть `CashShift`, но нет shift API и `ShiftId` не назначается при проведении | `backend/src/FaizaCash.Domain/Entities/Models.cs:15`; `backend/src/FaizaCash.Api/Controllers/ApiEndpoints.cs:186`; `FINAL_AUDIT_REPORT.md:145–153` |
| P0 | `BalanceLimit` хранится и валидируется на отрицательность, но правило лимита при проведении не применяется | `Models.cs:8`; `ApiEndpoints.cs:105–110`; `FINAL_AUDIT_REPORT.md:155–165` |
| P1 | Начальный остаток представлен `CashboxOpening`, но штатного API/UI workflow нет | `Models.cs:9`; `FINAL_AUDIT_REPORT.md:205–219` |
| P1 | Заказы, филиалы и складские сущности не представлены в доменной модели; есть касса, контрагент и документ | `Models.cs:8–19` |
| P1 | Production TLS-конфигурация подготовлена, но live certificate/proxy smoke не доказан | `Program.cs:16–29,58–60`; `appsettings.Production.json:1–6`; `docker-compose.production.yml:49–96`; `docs/deployment.md` |
| P1 | Browser E2E и ручная accessibility-приёмка не подтверждены | `README.md`, разделы о проверках и ограничениях; `FINAL_AUDIT_REPORT.md:225–236` |
| P1 | Backup/restore автоматизирован для изолированной `_test` БД, но production restore не проверен | `scripts/verify-backup.mjs:3–14`; `docs/backup.md` |

## Уже подтверждённо исправлено

FG-001–FG-011, FG-014, FG-015 и FG-017 отмечены в `FINAL_AUDIT_REPORT.md` как исправленные; текущий прогон также подтвердил build, unit 21/21, frontend 12/12, integration 26/26, security 14/14, catalog 92/92 и ledger 7/7.

## Не следует считать gap без дополнительного решения

Наличие смен, hard limit, отдельного Opening workflow, исторически неизменяемой печати и связки с заказами — это требования к согласованию, а не автоматически доказанные дефекты кода.
