# Проверки FAIZA GOLD

Дата: 23.11.2026. Проверки выполнены локально в Windows, Node v22.22.0, .NET SDK 10.0.203.

| Команда | Результат |
|---|---|
| `dotnet restore backend/FaizaCash.sln --locked-mode` | Завершён. NU1900: сервис NuGet vulnerability audit недоступен. |
| `dotnet build backend/FaizaCash.sln --no-restore --nologo` | PASS, 0 ошибок. |
| `dotnet test backend/tests/FaizaCash.UnitTests --no-build --nologo` | PASS, 27/27. |
| `npm --prefix frontend test` | PASS, 13/13. |
| `npm --prefix frontend run build` | PASS. Vite 7.2.2. |
| Latest source changes | Backend build and frontend production build pass after settlement cashbox scoping, notification scoping, and restore audit changes. Added API regression cases; PostgreSQL scenarios NOT RUN here. |
| Docker/PostgreSQL integration, security и E2E | BLOCKED/NOT RUN: Docker daemon недоступен; pg_isready отсутствует. Integration script выполняет DROP SCHEMA public в специально названной test DB, поэтому без проверки изолированности не запускался. |
| Backup restore implementation | Реализован для отдельного PostgreSQL из `ConnectionStrings:BackupRestore`; production Compose использует временный `tmpfs`, отдельную сеть и секрет. Проверяет checksum и список миграций. |
| Реальный backup restore | NOT RUN: Docker daemon недоступен; end-to-end восстановление и сверка данных не доказаны. |

### Read-only load probe

`tests/load-readonly.mjs` выполняет 10 000 GET-запросов к Dashboard при concurrency 20 по умолчанию и печатает throughput/p50/p95/p99. Требуется `TOKEN` с временным тестовым JWT. Для локального API задайте `API_URL=http://127.0.0.1:5085`; перед запуском используйте тестовую базу. Удалённые адреса заблокированы без явного `LOAD_ALLOW_REMOTE=1`. Сценарий не изменяет данные и не подтверждает финансовую конкурентность.

Успешная сборка не является доказательством корректности финансовых операций, безопасности API или готовности production.
