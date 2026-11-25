# Диаграммы состояний

## Кассовый документ

В модели статус проведения (`draft`/`posted`) и статус согласования (`none`/`pending`/`approved`/`rejected`, W1) — разные поля. Ниже они показаны как единый жизненный цикл, как его видит пользователь. Ветка согласования действует только для расходов с суммой не меньше порога.

```mermaid
stateDiagram-v2
    [*] --> Draft: создан
    Draft --> Draft: изменён (Version + 1)
    Draft --> [*]: удалён
    Draft --> Pending: отправлен на согласование (W1)
    Pending --> Approved: согласован руководителем ≠ автор (W1)
    Pending --> Rejected: отклонён с причиной (W1)
    Rejected --> Draft: изменён, статус согласования сброшен
    Approved --> Draft: изменён, статус согласования сброшен
    Draft --> Posted: проведён (расход ниже порога или приход)
    Approved --> Posted: проведён
    Posted --> Reversed: проведено сторно
    Posted --> [*]
    Reversed --> [*]

    state Posted {
        state "Неизменяем" as Immutable
        [*] --> Immutable
        note right of Immutable: триггер БД запрещает UPDATE и DELETE
    }
```

## Кассовая смена

```mermaid
stateDiagram-v2
    [*] --> Open: открыта, остаток = расчётному
    Open --> Open: проводятся документы на дату смены
    Open --> Closed: закрыта с фактическим остатком
    Closed --> [*]
    note right of Closed
        Разница = факт − расчёт
        W1: необязательный пересчёт по купюрам,
        сумма которого обязана совпасть с фактом
    end note
```

## Задолженность

```mermaid
stateDiagram-v2
    [*] --> open: создана
    open --> partially_paid: оплата < остатка
    open --> paid: оплата = остатку
    partially_paid --> partially_paid: ещё оплата < остатка
    partially_paid --> paid: оплата = остатку
    open --> cancelled: отменена (только без оплат)
    paid --> [*]
    cancelled --> [*]
```

## Квитанция торгового представителя

```mermaid
stateDiagram-v2
    [*] --> collected: собрано у клиента
    collected --> handed_over: передано в кассу
    handed_over --> accepted: принято кассиром
    collected --> cancelled: отменено
    handed_over --> cancelled: отменено
    accepted --> [*]
    cancelled --> [*]
    note right of accepted: отменить нельзя, вложения удалить нельзя
```

## Аванс подотчётному лицу (W1)

```mermaid
stateDiagram-v2
    [*] --> issued: создан из проведённого РКО
    issued --> reported: принят авансовый отчёт
    reported --> closed: остаток 0, или проведён ПКО возврата, или проведён РКО перерасхода
    closed --> [*]
    state issued {
        state "В срок" as OnTime
        state "Просрочен" as Overdue
        [*] --> OnTime
        OnTime --> Overdue: срок отчёта < сегодня
    }
```
