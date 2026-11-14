# Техническое задание проекта FAIZA GOLD / Faiza Cash v1

## 1. Назначение системы

Faiza Cash — веб-система автоматизации кассового учета организации. Система предназначена для:

- ведения кассовых ящиков и филиалов;
- регистрации приходных и расходных операций;
- контроля черновиков, проведения и сторнирования документов;
- ведения категорий, контрагентов и типов документов;
- учета смен, межкассовых переводов, задолженностей и платежей;
- формирования кассовой книги, CSV- и PDF-отчетов;
- управления пользователями, ролями, доступом к кассам и аудитом действий.

Система работает с PostgreSQL, предоставляет HTTP API и React-интерфейс.

## 2. Технологический стек

### Backend

- C# / .NET 10;
- ASP.NET Core Minimal API;
- Entity Framework Core 10;
- PostgreSQL через Npgsql;
- JWT access token и refresh token;
- QuestPDF для PDF-отчетов;
- xUnit для модульных тестов.

### Frontend

- React 19;
- Vite 7;
- JavaScript/JSX;
- `lucide-react` для иконок;
- `recharts` для графиков;
- Node.js test runner для frontend-тестов;
- Nginx в production-контейнере.

### Развертывание

- Docker Compose;
- отдельные контейнеры PostgreSQL, API и frontend;
- production reverse proxy через Caddy;
- отдельный мигратор базы данных.

## 3. Структура проекта

```text
backend/
  FaizaCash.sln
  src/
    FaizaCash.Api/            HTTP API, middleware, авторизация
    FaizaCash.Application/    DTO, интерфейсы, бизнес-правила
    FaizaCash.Domain/         доменные сущности и Money
    FaizaCash.Infrastructure/ EF Core, PostgreSQL, сервисы, миграции
    FaizaCash.Migrator/       применение миграций в deployment job
  tests/FaizaCash.UnitTests/  модульные тесты

frontend/
  src/app/                    корневое приложение и маршрутизация
  src/components/             переиспользуемые UI-компоненты
  src/pages/                  страницы приложения
  src/services/               API-клиент и контракты
  src/hooks/                  React hooks
  src/styles/                 стили и accessibility/print-правила
  tests/                      frontend-тесты

deploy/                       Caddy и SQL подготовки runtime-роли
scripts/                      локальный запуск, backup, demo и диагностика
tests/                        integration, security, catalog и ledger checks
cache/                        техническое задание и проектные материалы
```

## 4. Архитектура backend

### FaizaCash.Api

Точка входа — `Program.cs`. Компонент выполняет:

- загрузку конфигурации и строки подключения;
- регистрацию DbContext и application/infrastructure-сервисов;
- настройку CORS, JWT, rate limiting и forwarded headers;
- security-заголовки и глобальную обработку ошибок;
- применение миграций в Development/Test;
- регистрацию endpoint-групп.

Основные endpoint-модули:

- `ApiEndpoints.cs` — аутентификация, пользователи, кассы, операции, смены, переводы и платежи;
- `DirectoryEndpoints.cs` — категории и контрагенты;
- `ReportEndpoints.cs` — кассовая книга, CSV и PDF;
- `AuditEndpoints.cs` — журнал аудита.

Middleware:

- `RequestMiddleware` — обработка запросов и ошибок;
- `AccessMiddleware` — проверка доступа пользователя;
- `DirectoryCacheMiddleware` — кэширование справочников.

### FaizaCash.Application

Содержит контракты прикладного слоя:

- DTO запросов и ответов;
- интерфейсы `ILedgerService`, `ICashboxAccess`, `ICurrentActor`, `IPdfReport`;
- правила баланса и сторнирования;
- проекции данных для API.

### FaizaCash.Domain

Содержит доменную модель и value object `Money`. Основные сущности:

- `User`, `Role`, `UserRole`, `RefreshToken`, `LoginAttempt`;
- `Organization`, `Currency`, `Branch`, `Cashbox`, `CashboxOpening`;
- `Category`, `Counterparty`, `DocumentType`;
- `CashOperation`, `DocumentSequence`, `CashShift`;
- `CashTransfer`, `Settlement`, `PaymentAllocation`;
- `RepresentativeReceipt`, `CashAccess`, `AuditEvent`.

### FaizaCash.Infrastructure

Реализует:

- `CashDb` и конфигурацию Entity Framework;
- PostgreSQL persistence;
- identity и password hashing;
- ledger и контроль доступа к кассам;
- PDF-генерацию;
- JSON-файловое логирование;
- Design-time factory;
- полный набор EF Core migrations и model snapshot.

## 5. Данные и бизнес-правила

1. Денежные значения хранятся с точностью до двух знаков после запятой.
2. Сумма операции должна быть положительной и находиться в допустимом диапазоне.
3. Операция бывает `draft` или `posted`.
4. Проведенная операция получает номер документа, posting sequence, дату и автора проведения.
5. Проведенные документы защищены от удаления и несанкционированного изменения.
6. Сторно связано с исходной операцией и содержит причину.
7. Для операций используется idempotency через `RequestKey`.
8. Перевод между кассами создает связанные операции источника и назначения.
9. Смена кассы имеет статус `open` или `closed`, начальный и фактический остаток.
10. Доступ к кассе проверяется по связке пользователя и cashbox.
11. Изменения данных записываются в `AuditEvent`.
12. Ограничения уникальности и допустимых значений дополнительно контролируются PostgreSQL.

## 6. Пользователи и безопасность

Роли приложения:

- `admin` — администрирование пользователей, филиалов, касс и справочников;
- `accountant` — учетные операции, справочники и отчеты;
- `cashier` — кассовые операции в разрешенной кассе;
- `auditor` — просмотр и аудит без изменения данных.

Требования безопасности:

- JWT issuer, audience и signing key обязательны;
- ключ JWT должен содержать не менее 32 символов;
- refresh tokens хранятся в виде hash и могут быть отозваны/заменены;
- вход ограничен rate limiter;
- production требует secure refresh cookie и доверенные proxy;
- включаются CSP, HSTS, X-Frame-Options и другие security headers;
- пароль не должен попадать в аудит и логи.

## 7. Frontend-функции

Интерфейс включает:

- страницу входа и смены пароля;
- dashboard с остатками, приходами, расходами и денежным потоком;
- операции и диалоги создания/изменения;
- управление пользователями и доступом к кассам;
- отчеты и фильтры;
- аудит действий;
- филиалы, смены, межкассовые переводы;
- задолженности и оплаты;
- представительские receipts;
- темную тему, адаптивную верстку и print-стили.

API-клиент централизован в `frontend/src/services/api.js`; контракты ответов проверяются через `contracts.js`.

## 8. База данных и миграции

PostgreSQL содержит схему организации, пользователей, ролей, касс, операций, смен, переводов, расчетов и аудита.

Миграции находятся в `backend/src/FaizaCash.Infrastructure/Migrations` и применяются:

- автоматически в Development/Test;
- отдельным `FaizaCash.Migrator` в production.

Удаление миграций запрещено: они являются частью истории схемы и необходимы для воспроизводимого развёртывания.

## 9. Запуск и развертывание

### Локальный backend

```powershell
dotnet restore backend/FaizaCash.sln
dotnet build backend/FaizaCash.sln
dotnet test backend/FaizaCash.sln
node scripts/run-local.mjs
```

### Локальный frontend

```powershell
cd frontend
npm ci
npm run dev
```

### Docker Compose

`docker-compose.yml` запускает PostgreSQL, API и frontend. Необходимые переменные:

- `POSTGRES_PASSWORD`;
- `JWT__KEY`;
- `SEED__ADMINPASSWORD`.

Production использует `docker-compose.production.yml`, мигратор и `deploy/Caddyfile`.

## 10. Тестирование

Backend:

- `dotnet test backend/FaizaCash.sln`;
- BalanceRules, Money, RefreshCookie и ShiftRules.

Frontend:

- `npm test`;
- API refresh/retry;
- контраст тем;
- demo data;
- server-side render и UX-контракты.

Integration suites:

- `tests/integration.mjs`;
- `tests/security.mjs`;
- `tests/catalog.mjs`;
- `tests/ledger-integrity.mjs`;
- `tests/run-all.mjs`.

## 11. Нефункциональные требования

- сохранность финансовых данных и audit trail;
- запрет удаления рабочих данных через очистку проекта;
- транзакционная целостность операций;
- повторяемое применение миграций;
- ограничение размера HTTP body 6 MB;
- экспорт не более 10 000 строк за один запрос;
- доступ к данным только после аутентификации и проверки cashbox scope;
- production запускается только при корректной security-конфигурации.

## 12. Ограничения сопровождения

- не удалять рабочие C# и frontend-файлы без подтвержденного отсутствия ссылок;
- не удалять миграции, SQL подготовки ролей и конфигурацию deployment;
- не хранить секреты в Git;
- не удалять резервные копии PostgreSQL;
- после изменений выполнять restore, build, unit tests и frontend build.

Документ составлен по фактической структуре и исходному коду проекта на ветке `cleanup/faiza-gold`.
