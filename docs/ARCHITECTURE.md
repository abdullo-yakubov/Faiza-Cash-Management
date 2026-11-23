# Архитектура FAIZA GOLD — фактическое состояние

Система развёртывается как веб-клиент, единый ASP.NET Core API и PostgreSQL. Это естественная основа для Modular Monolith; микросервисы не требуются.

```text
React 19 + Vite
      │ HTTP API
ASP.NET Core API / Minimal API / middleware
      ├── FaizaCash.Domain          сущности и денежные правила
      ├── FaizaCash.Application     DTO, интерфейсы, application rules
      └── FaizaCash.Infrastructure  EF Core, Npgsql, PostgreSQL, PDF, backup, logging
                     │
              FaizaCash.Migrator
                     │
                PostgreSQL
```

Направление целевой зависимости: API → Application → Domain; Infrastructure реализует порты Application и использует Domain. API composition root подключает Infrastructure через DI. Присутствуют отдельные проекты Domain/Application/Infrastructure/API/Migrator. При этом API endpoint файлы содержат большой объём бизнес-обработчиков и используют EF непосредственно; значит, границы Clean Architecture соблюдены частично. Переносить сценарии следует постепенно, после тестов финансовых инвариантов.

## Предметные модули, обнаруженные в коде

- Identity & Access: users, roles, JWT/refresh, доступы к кассам.
- Organization: филиалы, кассы, валюты и настройки.
- Cash Operations: операции, проведение и сторно.
- Shifts: открытие/закрытие смен.
- Transfers: связанные кассовые движения.
- Settlements: задолженности закрепляются за кассой; выдача списков, отчётов, уведомлений и dashboard summaries ограничена доступными пользователю кассами. Исторические незакреплённые задолженности доступны только администратору, пока их нельзя безопасно привязать автоматически.
- Representatives: receipts с переходами collected/handed_over/accepted.
- Reporting: dashboard, кассовая книга, PDF/CSV и аналитика.
- Audit & Notifications.
- Backup Operations: создание и проверка; test restore выполняется только в отдельный PostgreSQL из `ConnectionStrings:BackupRestore`, сверяет SHA-256 и список миграций. Production Compose задаёт отдельный контейнер с эфемерным `tmpfs` и собственным секретом. Фактический runtime restore должен быть проверен при доступном Docker daemon.

## Важные архитектурные ограничения

1. Проверка auth/RBAC/object scope распределена по endpoint атрибутам и `AccessMiddleware`/`ICashboxAccess`; нужна маршрутная матрица с негативными тестами.
2. EF Core/PostgreSQL инварианты включают `decimal(18,2)`, check/unique/FK constraints, concurrency token и выборочные row locks; системное конкурентное доказательство отсутствует.
3. Migrator отдельный, а production Compose предусматривает отдельные migrator/runtime credentials.
4. Restore обязан использовать изолированную disposable database; нельзя выдавать runtime production роли `CREATEDB` ради тестового сценария.
5. Это схема по исходникам, а не отчёт о выполненном runtime/deployment тесте.
