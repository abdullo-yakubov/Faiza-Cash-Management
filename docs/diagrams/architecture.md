# Архитектура и развёртывание

## Логическая архитектура (слои)

Зависимости направлены внутрь: `Api → Infrastructure → Application → Domain`. Domain не зависит ни от чего.

```mermaid
flowchart TB
    subgraph Client[Браузер]
        SPA[React 19 SPA<br/>hash-маршрутизация, Recharts]
    end

    subgraph Api[FaizaCash.Api — ASP.NET Core 10 Minimal API]
        MW[Middleware:<br/>Request → Auth → RateLimit → Access → DirectoryCache]
        EP[Группы endpoint-ов:<br/>операции, смены, переводы, расчёты,<br/>справочники, отчёты, аудит, копии]
    end

    subgraph App[FaizaCash.Application]
        RULES[Бизнес-правила:<br/>остаток, смена, сторно]
        DTO[DTO и интерфейсы:<br/>доступ к кассе, проведение, PDF]
    end

    subgraph Infra[FaizaCash.Infrastructure]
        EF[EF Core 10 + Npgsql<br/>контекст БД, миграции]
        LED[Сервис проведения]
        BK[Резервное копирование<br/>AES-256-GCM]
        PDF[PDF-отчёты QuestPDF]
        ID[Identity: JWT, PBKDF2, refresh]
    end

    subgraph Dom[FaizaCash.Domain]
        ENT[Сущности, Money]
    end

    DB[(PostgreSQL 18<br/>триггеры неизменяемости)]
    FS[(Файловое хранилище<br/>копии, вложения W1)]

    SPA -->|HTTPS JSON, Bearer JWT,<br/>refresh-cookie| MW
    MW --> EP
    EP --> RULES
    EP --> DTO
    EP --> EF
    EP --> LED
    LED --> RULES
    LED --> EF
    EF --> ENT
    RULES --> ENT
    EF --> DB
    BK --> DB
    BK --> FS
    PDF --> DTO
```

## Развёртывание (production Compose)

```mermaid
flowchart LR
    USER[Пользователи филиалов<br/>браузер]

    subgraph Host[Сервер, Docker Compose]
        subgraph web[сеть web]
            PROXY[proxy<br/>Caddy 2.10<br/>TLS :80/:443]
            FE[frontend<br/>nginx + статические файлы]
        end
        API[api<br/>ASP.NET Core :8080<br/>роль БД faiza_runtime]
        subgraph data[сеть data, internal]
            DBP[(db<br/>PostgreSQL 18.3<br/>том postgres-production)]
            MIG[migrate<br/>одноразовый,<br/>роль faiza_migrator]
            PROV[provision-runtime<br/>одноразовый,<br/>создаёт роль runtime]
        end
        subgraph restore[сеть restore, internal]
            RDB[(backup-restore-db<br/>PostgreSQL на tmpfs)]
        end
        VOL[(том backup-data<br/>зашифрованные копии)]
    end

    USER -->|HTTPS| PROXY
    PROXY -->|/| FE
    PROXY -->|/api| API
    API --> DBP
    API -->|тест восстановления| RDB
    API --> VOL
    MIG -->|1. применить миграции| DBP
    PROV -->|2. роль с минимальными правами| DBP
```

Порядок запуска: `db` (healthy) → `migrate` (успешно завершён) → `provision-runtime` (успешно завершён) → `api` (вместе с healthy `backup-restore-db`) → `frontend`, `proxy`. API не стартует при неприменённых миграциях. Сети `data` и `restore` внутренние — база недоступна извне.

Для вложений волны 1 (ADR 0008) потребуется отдельный том, смонтированный в контейнер `api`; его добавляет агент backend (`docker-compose.production.yml` вне зоны документации).
