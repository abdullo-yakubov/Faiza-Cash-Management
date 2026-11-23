# Проверки FAIZA GOLD

Дата: 23.11.2026. Проверки выполнены локально в Windows, Node v22.22.0, .NET SDK 10.0.203.

| Команда | Результат |
|---|---|
| `dotnet restore backend/FaizaCash.sln --locked-mode` | Завершён. NU1900: сервис NuGet vulnerability audit недоступен. |
| `dotnet build backend/FaizaCash.sln` | PASS, 0 ошибок. NU1900: NuGet vulnerability feed недоступен в этой среде. |
| `dotnet test backend/FaizaCash.sln` | PASS, 33/33 unit-тестов, включая тесты суммы прописью. |
| `npm --prefix frontend test` | PASS, 13/13. |
| `npm --prefix frontend run build` | PASS. Vite 7.2.2. |
| PDF ордер / кассовая книга | PDF-генератор обновлён: сумма прописью для TJS/RUB, строки подписи кассира и бухгалтера. Проверена сборка и unit-тесты форматтера; PDF на реальной PostgreSQL записи вручную не открывался. |
| Frontend theme/assets | Production build прошёл после удаления Google Fonts CDN; 4 скриншота страницы входа проверены в браузере: desktop 1440×900 и mobile 390×844, light/dark. Dashboard/операции/PDF screenshot и Lighthouse NOT RUN: API/БД не запущены. |
| Latest source changes | Backend build, unit-тесты и frontend test/build выполнены после последних изменений. PostgreSQL сценарии NOT RUN. |
| Demo scripts | `node --check scripts/seed-demo.mjs` и `node --check scripts/seed-demo-users.mjs` завершились без ошибок. Создание пользователей/API-сценарий NOT RUN без БД. |
| Screenshots | `docs/screenshots/login-desktop-light.png`, `login-desktop-dark.png`, `login-mobile-light.png`, `login-mobile-dark.png`. Все снимки страницы входа пустые, без сохранённых логинов. |
| Docker/PostgreSQL integration, security и E2E | BLOCKED/NOT RUN: `docker info` не подключился к Docker Engine (`permission denied` на named pipe); `pg_isready` отсутствует. Integration script выполняет DROP SCHEMA public в специально названной test DB, поэтому без проверки изолированности не запускался. |
| Backup restore implementation | Реализован для отдельного PostgreSQL из `ConnectionStrings:BackupRestore`; production Compose использует временный `tmpfs`, отдельную сеть и секрет. Проверяет checksum и список миграций. |
| Реальный backup restore | NOT RUN: Docker daemon недоступен; end-to-end восстановление и сверка данных не доказаны. |

### Read-only load probe

`tests/load-readonly.mjs` выполняет 10 000 GET-запросов к Dashboard при concurrency 20 по умолчанию и печатает throughput/p50/p95/p99. Требуется `TOKEN` с временным тестовым JWT. Для локального API задайте `API_URL=http://127.0.0.1:5085`; перед запуском используйте тестовую базу. Удалённые адреса заблокированы без явного `LOAD_ALLOW_REMOTE=1`. Сценарий не изменяет данные и не подтверждает финансовую конкурентность.

Успешная сборка не является доказательством корректности финансовых операций, безопасности API или готовности production.
