# Последовательности «Согласование расхода» и «Цикл аванса» (волна 1)

Целевое поведение по API-контракту волны 1.

## Согласование расхода

```mermaid
sequenceDiagram
    autonumber
    actor C as Кассир (автор)
    actor M as Руководитель
    participant API as API
    participant DB as PostgreSQL

    C->>API: POST /api/operations (расход 12 000.00)
    API->>DB: INSERT черновик, approvalStatus = none
    API-->>C: 201 черновик
    C->>API: POST /api/operations/{id}/post
    API-->>C: 409 «Расход требует согласования руководителя»
    C->>API: POST /api/operations/{id}/submit-approval
    API->>DB: approvalStatus = pending, аудит
    API-->>C: 200
    M->>API: GET /api/approvals?status=pending
    API-->>M: Список документов на согласовании
    alt руководитель согласует
        M->>API: POST /api/operations/{id}/approve { comment }
        API->>API: Руководитель ≠ автор? иначе 409
        API->>DB: approvalStatus = approved, approvedBy, approvedAt, аудит
        API-->>M: 200
        C->>API: POST /api/operations/{id}/post
        API->>DB: Проведение (см. sequence-posting.md)
        API-->>C: 200 проведён
    else руководитель отклоняет
        M->>API: POST /api/operations/{id}/reject { reason }
        API->>DB: approvalStatus = rejected, approvalComment = reason, аудит
        API-->>M: 200
        C->>API: PATCH /api/operations/{id} (исправление)
        API->>DB: approvalStatus = none, Version + 1
        Note over C,API: Повторная отправка на согласование
    end
```

## Цикл аванса подотчётному лицу

```mermaid
sequenceDiagram
    autonumber
    actor A as Бухгалтер
    actor E as Сотрудник (подотчётное лицо)
    participant API as API
    participant DB as PostgreSQL

    A->>API: Создать и провести РКО на контрагента вида employee (5 000.00)
    API-->>A: РКО проведён
    A->>API: POST /api/advances { operationId, purpose, dueDate }
    API->>API: РКО проведён? контрагент — employee? РКО ещё не использован?
    API->>DB: INSERT аванс, status = issued, аудит
    API-->>A: 201 аванс
    Note over E: Тратит деньги, приносит чеки
    A->>API: POST /api/attachments (ownerType = advance, скан чека)
    A->>API: POST /api/advances/{id}/report { lines }
    API->>DB: INSERT строки отчёта, status = reported, аудит
    API-->>A: { spent: 4 200.00, remainder: 800.00 }
    alt remainder > 0 (возврат остатка)
        E->>A: Возвращает 800.00
        A->>API: Создать и провести ПКО от сотрудника на 800.00
        A->>API: POST /api/advances/{id}/settle { operationId: ПКО }
        API->>API: ПКО проведён, тот же сотрудник, сумма = остатку?
    else remainder < 0 (перерасход)
        A->>API: Провести РКО сотруднику на сумму перерасхода
        A->>API: POST /api/advances/{id}/settle { operationId: РКО }
    else remainder = 0
        A->>API: POST /api/advances/{id}/settle {}
    end
    API->>DB: status = closed, аудит
    API-->>A: 200 аванс закрыт
```

Аванс в статусе `issued`, у которого срок отчёта раньше сегодняшней даты (Asia/Dushanbe), считается просроченным и учитывается в показателе `overdueAdvances` панели.
