# Тестирование FAIZA GOLD

Документ описывает уровни тестов, их запуск, требования к окружению, порядок добавления тестов и трассировку требований ТЗ (`docs/ТЗ_ГОСТ_34.602-2020.md`) к автоматическим проверкам.

## 1. Уровни тестов

| Уровень | Набор | Инструмент | Что проверяет | Нужна БД | Нужен Docker |
|---|---|---|---|:-:|:-:|
| Модульный | `backend/tests/FaizaCash.UnitTests` | xUnit | `Money`, `BalanceRules`, `ShiftRules`, шифрование копий, refresh-cookie, сумма прописью | — | — |
| Клиент | `frontend/tests/*.test.js` | `node --test` | API-клиент (обновление токена), рендер, UX, контраст, CSP-заголовки, демо-данные | — | — |
| API: основной | `tests/integration.mjs` | Node.js + fetch | Проведение, смены, лимиты, деньги, идемпотентность, версии, гонки, сторно, отчёты, PDF, аудит, переводы, задолженности | ✓ `_test` | — |
| API: безопасность | `tests/security.mjs` (`tests/security/cases.mjs`) | Node.js | Анонимный доступ, доступ к чужой кассе, эскалация ролей, CSRF, ротация refresh, сброс пароля, дубли логинов | ✓ | — |
| API: справочники | `tests/catalog.mjs` | Node.js | Валидация, нормализация, дубли, конкурентное создание, кэш | ✓ | — |
| API: целостность журнала | `tests/ledger-integrity.mjs` | Node.js + SQL | Неизменяемость, повторное проведение, откат при сбое аудита, агрегаты | ✓ | — |
| Приоритетные сценарии | `tests/integration/p0.mjs`, `p1.mjs`, `p2.mjs` | Node.js | Начальный остаток, аудит, лимиты частоты, отчёты, матрица «роль × маршрут», нагрузка p95, шифрование копий | ✓ | — |
| Инварианты | `tests/integration/invariants.mjs` | Node.js + SQL | SQL-оракул = API остатка = панель = тренд = кассовая книга = сводка = CSV | ✓ | — |
| Восстановление | `tests/integration/restore.mjs` | Node.js + Docker | Два восстановления подряд, очистка, отказ при подмене | ✓ | ✓ |
| Демо-наполнение | `tests/integration/demo-seed.mjs` | Node.js | Сценарий `seed-demo` на пустой базе | ✓ | — |
| E2E | `tests/e2e.mjs` | Playwright | Браузерные маршруты кассира, бухгалтера, администратора; негативные сценарии; доступность WCAG 2.2 A/AA | ✓ | — |
| Нагрузка (ручной) | `tests/load-readonly.mjs` | Node.js | Чтение под нагрузкой (не входит в `run-all`) | ✓ | — |
| CI | `.github/workflows/verify.yml` | GitHub Actions | restore (locked), build, unit, frontend test/build, `npm audit`, `dotnet format` и ESLint (в режиме предупреждения) | — | — |

## 2. Требования к окружению

- .NET SDK 10.0.203 (`backend/global.json`), Node.js 22, PostgreSQL 18 с клиентскими утилитами (`pg_dump`, `pg_restore`).
- Локальная конфигурация создаётся `node scripts/configure-local.mjs` (две базы: разработки и с суффиксом **`_test`**).
- **Все API-наборы работают только с базой, имя которой оканчивается на `_test`**: наборы пересоздают её схему. Имя проверяется перед запуском. Никогда не направляйте тесты на демо- или рабочую базу.
- `restore.mjs` поднимает одноразовый контейнер `postgres:18.3-alpine` на `127.0.0.1:55432` — нужен запущенный Docker Desktop.
- E2E: браузер Playwright (`npx playwright install chromium` один раз).
- Каждый набор поднимает собственный экземпляр API на отдельном порту (5084–5089), поэтому не запускайте наборы параллельно — они делят одну схему `_test`.
- Секреты (`.runtime/local.json`, `.env`) в тестах читаются, но не выводятся; не прикладывайте их к отчётам.

## 3. Запуск

Из корня репозитория:

| Что | Команда |
|---|---|
| Всё по порядку (итог `All suites passed.`) | `node tests/run-all.mjs` |
| Сборка | `dotnet build backend/FaizaCash.sln` |
| Модульные | `dotnet test backend/FaizaCash.sln` |
| Один модульный тест | `dotnet test backend/FaizaCash.sln --filter "FullyQualifiedName~BalanceRulesTests"` |
| Клиент | `npm --prefix frontend test` |
| Отдельный API-набор | `node tests/integration.mjs --suite=integration`, `node tests/security.mjs`, `node tests/catalog.mjs`, `node tests/ledger-integrity.mjs`, `node tests/integration/p0.mjs` … |
| E2E | `node tests/e2e.mjs` (или `npm --prefix frontend run test:e2e`) |
| Форматирование C# (проверка) | `dotnet format backend/FaizaCash.sln --verify-no-changes` |
| Линтер клиента | `npx eslint frontend/src frontend/tests` (после установки devDependencies, см. `REPORT-C.md`) |

`run-all.mjs` не останавливается на упавшем наборе (кроме сборки) и печатает список упавших в конце; код выхода ненулевой, если хоть один упал. Результаты xUnit сохраняются в `.runtime/unit-results/unit-tests.trx`.

### Фактические результаты

Таблицу заполняет тот, кто запускает полный прогон (в волне 1 — агент backend), после каждого значимого изменения:

| Дата | Ветка / коммит | Набор | Результат |
|---|---|---|---|
| `[заполнить]` | | `run-all.mjs` | |

## 4. Как добавить тест

### 4.1. На бизнес-правило (модульный)

1. Правило должно быть чистой функцией в слое Application (как `BalanceRules.CanPost`, `ShiftRules.CanPost`, `ReversalRules.MatchesOriginal`) — без БД и `HttpContext`.
2. Создайте или дополните класс `<Правило>Tests.cs` в `backend/tests/FaizaCash.UnitTests`.
3. Имя теста — утверждение о поведении: `ExpenseCannotExceedOpeningBalance`, `SpendingExactlyTheBalanceIsAllowed`.
4. Проверяйте границы: ровно на пороге, на копейку больше/меньше, пустая касса, будущая/прошлая дата. Для набора значений — `[Theory]` + `[InlineData]`.
5. Деньги задавайте литералами `decimal` (`100.10m`), не `double`.

Пример для правила волны 1:

```csharp
public sealed class ApprovalRulesTests
{
    [Theory]
    [InlineData("9999.99", false)]
    [InlineData("10000.00", true)]   // «не меньше порога» — граница включается
    public void ExpenseAtOrAboveThresholdRequiresApproval(string amount, bool required) =>
        Assert.Equal(required, ApprovalRules.Required("expense", decimal.Parse(amount, CultureInfo.InvariantCulture), threshold: 10000.00m));
}
```

### 4.2. На сценарий API (интеграционный)

1. Выберите набор по смыслу: проведение и журнал — `tests/integration.mjs`; права и сессии — `tests/security/cases.mjs`; справочники — `tests/catalog.mjs`; инварианты остатка — `tests/integration/invariants.mjs`; новые функции — `p0`/`p1`/`p2` или новый файл `tests/integration/<функция>.mjs` на общем `harness.mjs` (и добавьте его в `tests/run-all.mjs`).
2. Используйте `startApi({port, name})` из `harness.mjs` (свободный порт!), создавайте собственные кассы/категории с уникальным суффиксом — не полагайтесь на данные других тестов.
3. Проверяйте **и код ответа, и отсутствие побочного эффекта**: после 409 остаток, статус документа и счётчик номеров не изменились.
4. Для прав — отдельный пользователь нужной роли с доступом только к нужной кассе; проверяйте и «чужую» кассу.
5. Для гонок — `Promise.all` двух одинаковых запросов, ожидание `[200, 409]` после сортировки.
6. Название теста — с идентификатором требования, если он есть: `'Ф-09 author cannot approve own expense (409)'`.
7. Объявляйте `x-faiza-access` у каждого нового endpoint-а: GET-маршруты без параметров пути автоматически попадают в тест матрицы «роль × маршрут» (`p2.mjs`) через OpenAPI. Мутирующие маршруты (POST/PATCH/PUT/DELETE) и маршруты с `{id}` этим тестом не охвачены — для них пишите явные проверки 403 по ролям.

## 5. Трассировка «требование ТЗ → тест»

Обозначения требований — по `docs/ТЗ_ГОСТ_34.602-2020.md`. Для функций волны 1 тесты добавляет агент backend; столбец заполнен ожидаемыми проверками и подлежит обновлению после слияния (`ожидается`).

| Требование | Суть | Тест(ы) | Статус |
|---|---|---|---|
| п. 4.1.4 ПН-2 | p95 панели ≤ 500 мс при 20 читателях | `p2.mjs` — NFR-01/03 Dashboard under 20 concurrent readers | есть |
| п. 4.1.4 ПН-3, ПН-4 | Сумма > 0, 2 знака, максимум | `MoneyTests`; `integration.mjs` — Reject zero, negative and sub-cent amounts; e2e — amount with three decimals | есть |
| п. 4.1.4 ПН-5, ПН-6 | Ограничение страницы и выгрузки | `p1.mjs` — Lists are bounded; `integration.mjs` — CSV and PDF exports reject more than 10000 rows | есть |
| п. 4.1.5 (1), К-3 | Конкурентные расходы не уводят в минус | `integration.mjs` — Concurrent expenses cannot jointly overdraw; `ledger-integrity.mjs` — concurrent expenses | есть |
| п. 4.1.5 (2) | Идемпотентность ключа запроса | `integration.mjs` — Idempotent submission; Idempotency compares the full payload; `security` — Reused operation key; D-05 receipts | есть |
| п. 4.1.5 (3) | Проверка версии черновика | `integration.mjs` — Posting rejects a stale version; `ledger-integrity.mjs` — stale posting confirmation | есть |
| п. 4.1.5 (4) | Сбой аудита откатывает проведение | `integration.mjs` — Audit failure rolls back posting; `ledger-integrity.mjs` — audit failure rolls back | есть |
| п. 4.1.7 (3) | Контраст AA | `contrast.test.js` | есть |
| п. 4.1.7 (5) | Фильтры в URL | `ux.test.js` — route roundtrip preserves filters | есть |
| п. 4.1.7 (6), К-14 | Доступность | `e2e.mjs` — accessibility (WCAG 2.2 A/AA, automated part) | есть |
| п. 4.1.9 (4) | Ротация и отзыв refresh, Secure-cookie | `security` — Refresh rotates, reuse revokes; `RefreshCookieTests` | есть |
| п. 4.1.9 (5) | Ограничение частоты 429 | `p1.mjs` — D-06 change-password and refresh are rate limited | есть |
| п. 4.1.9 (6) | Одинаковое время для неизвестного логина | `p1.mjs` — D-07 unknown login costs the same | есть |
| п. 4.1.9 (7) | Отзыв доступа действует сразу | `security` — Cashier access grants and revocations…; `catalog.mjs` — disabled user denied despite valid JWT | есть |
| п. 4.1.9 (8), К-4 | Матрица «роль × маршрут × касса» | `p2.mjs` — Role × route × scope matrix (только GET без `{id}`); `security` — All five roles stay scoped | частично: мутирующие маршруты не охвачены |
| п. 4.1.9 (9), К-5 | Admin без финансовых операций | явный тест 403 для admin по каждой финансовой мутации (матрица `p2.mjs` их не охватывает) | ожидается |
| п. 4.1.9 (10) | Заголовки CSP и др. | `headers.test.js` | есть |
| п. 4.1.9 (11) | CSRF / Origin | `security` — Cookie actions require CSRF header | есть |
| п. 4.1.9 (15) | CSV-формулы нейтрализуются | `p1.mjs` — CSV exports neutralise formulas | есть |
| п. 4.1.10 (1), К-2 | Неизменяемость на уровне БД | `ledger-integrity.mjs` — reversal is immutable…; `integration.mjs` — Posted documents cannot be edited or deleted; ручной SQL на защите | есть |
| п. 4.1.10 (2–4), К-11 | Шифрование, проверка, тест восстановления | `BackupCryptoTests`; `p2.mjs` — D-09 (два теста); `restore.mjs` (четыре теста) | есть |
| Ф-04 | Начальный остаток один раз, только admin | `p0.mjs` — FG7-001 opening API is admin-only, one-time; e2e — admin sets initial balance | есть |
| Ф-05 | Справочники, дубли | `catalog.mjs` (все проверки) | есть |
| Ф-05 (системные категории) | Нельзя переименовать/архивировать (409); перевод использует их | ожидается | ожидается |
| Ф-06 | Курсы, номиналы | ожидается | ожидается |
| Ф-07 | Смена: одна открытая, остаток = расчётному, разница | `ShiftRulesTests`; `p0.mjs` — D-03; `invariants.mjs` — Closed shifts reconcile | есть |
| Ф-07 (пересчёт) | Сумма купюр ≠ факту → 400 | ожидается | ожидается |
| Ф-08 | Черновик, валидация | `integration.mjs`; `security` — Cashier cannot edit another author's draft | есть |
| Ф-09, К-6 | Согласование: порог, автор ≠ согласующий, сброс при изменении | ожидается | ожидается |
| Ф-10 | Проведение: смена, остаток на любую дату, лимит, нумерация, снимки | `BalanceRulesTests` (7); `integration.mjs` — Posting requires an open shift, Income above the limit, Repeated and simultaneous posting, Voucher PDFs use captured names, Posting is rejected after shift closure | есть |
| Ф-11 | Сторно | `integration.mjs` — Reversal is linked, requires reason; `invariants.mjs` — Reversal nets to zero | есть |
| Ф-12 | Перевод | `integration.mjs` — Transfers require open shifts on both; `invariants.mjs` — Transfer −X/+X | есть |
| Ф-12 (валюты) | Перевод между валютами → 409 | ожидается | ожидается |
| Ф-13, К-7 | Вложения: сигнатура, 5 МБ, 10 файлов, удаление до проведения | ожидается | ожидается |
| Ф-14 | Задолженности и платежи | `integration.mjs` — Settlement filters…; `invariants.mjs` — Payments never exceed the debt; `p1.mjs` — cancelling | есть |
| Ф-15 | Квитанции | `p1.mjs` — Representative report, cancelling receipts; `security` — D-05 | есть |
| Ф-16, К-8 | Авансы: создание из проведённого РКО на employee, отчёт, закрытие по остатку, просрочка | ожидается | ожидается |
| Ф-18, К-9 | Акт инвентаризации неизменяем | ожидается | ожидается |
| Ф-19 | Отчёты CSV/PDF | `integration.mjs` — Server exports real PDF; `p1.mjs` — Shift report, Representative report | есть |
| Ф-20, К-10 | Согласованность сумм в представлениях | `invariants.mjs` — Every cashbox: SQL oracle = …; Dashboard total = sum of balances; `p1.mjs` — attention block | есть (дополнить новыми полями) |
| Ф-21 | Уведомления видимы пользователю | `integration.mjs` — Notifications cannot be marked read unless visible | есть |
| Ф-22 | Аудит, фильтры | `p0.mjs` — D-04; `p2.mjs` — Audit filter accepts bare dates; `integration.mjs` — Audit records posting | есть |
| Ф-24 | Панель состояния только admin | ожидается | ожидается |
| Ф-25 | OpenAPI только admin, x-faiza-access | `p2.mjs` — OpenAPI document is admin-only | есть |
| п. 4.1.14 (2) | Демо-данные | `demo-seed.mjs`; `demo.test.js` | есть |

## 6. Что не покрыто автоматически

- Production-развёртывание с TLS на реальном домене (Caddy, HSTS) — проверяется вручную по руководству администратора.
- Печать PDF на физическом принтере, визуальное соответствие формам РТ.
- Работа в браузерах, кроме Chromium (E2E запускается в Chromium).
