# Аудит финансовой корректности

## PASS

- Деньги используют `decimal`: `backend/src/FaizaCash.Domain/Entities/Models.cs:8–15` и `ValueObjects/Money.cs`.
- Проведение блокирует строку кассы через `FOR UPDATE`: `backend/src/FaizaCash.Infrastructure/Services/LedgerService.cs:13`.
- Повторное проведение, stale confirmation, конкурентный overdraft, reversal и агрегаты подтверждены 7 ledger checks и 26 integration checks.
- Отчёт summary использует `RepeatableRead`: `backend/src/FaizaCash.Api/Controllers/ApiEndpoints.cs:203–213`.

## BLOCKED / не завершено

Правила смены и лимита не определены заказчиком и потому не могут считаться финансово завершёнными. В `CashShift` нет завершённого жизненного цикла, а `BalanceLimit` не участвует в `LedgerService`.
