import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../lib/api.js'
import { dateTime, kindNames, money, parseAmount, time } from '../lib/format.js'
import { Alert, Empty, Field, Modal, PageHead, useLoad, useSubmit } from '../components/ui.jsx'
import OrderPrint from '../components/OrderPrint.jsx'

export default function Cash() {
  const [params, setParams] = useSearchParams()
  const registers = useLoad(() => api('/registers'), [])
  const directories = useLoad(() => Promise.all([api('/categories'), api('/counterparties')]), [])
  const active = useMemo(() => (registers.data || []).filter((r) => r.isActive), [registers.data])
  const registerId = Number(params.get('register')) || active[0]?.id
  const register = active.find((r) => r.id === registerId)

  const shift = useLoad(() => (registerId ? api(`/registers/${registerId}/shift`) : Promise.resolve(null)), [registerId])
  const ops = useLoad(() => (registerId ? api(`/operations?registerId=${registerId}`) : Promise.resolve([])), [registerId])
  const [closing, setClosing] = useState(false)
  const [reversing, setReversing] = useState(null)
  const [printing, setPrinting] = useState(null)

  const refresh = () => Promise.all([registers.reload(), shift.reload(), ops.reload()])

  const openShift = useSubmit(async () => {
    await api(`/registers/${registerId}/shift/open`, { method: 'POST' })
    await refresh()
  })

  if (registers.loading && !registers.data) return <div className="muted">Загрузка…</div>
  if (active.length === 0) return <Empty>Вам не назначена ни одна касса. Обратитесь к администратору.</Empty>

  return (
    <>
      <PageHead title="Касса">
        <select value={registerId} onChange={(e) => setParams({ register: e.target.value })} aria-label="Касса">
          {active.map((r) => (
            <option key={r.id} value={r.id}>{r.branchName} / {r.name} ({r.currency})</option>
          ))}
        </select>
      </PageHead>
      <Alert error={registers.error || shift.error || openShift.error} />

      {register && (
        <div className="cash-top">
          <div className="card balance-card">
            <div className="muted">Остаток в кассе</div>
            <div className="big-balance">{money(register.balance, register.currency)}</div>
            {shift.data ? (
              <div className="shift-info">
                <span className="badge badge-ok">Смена открыта</span>
                <span className="muted">с {dateTime(shift.data.openedAt)}, {shift.data.openedBy}</span>
                <div className="shift-sums">
                  <span>На начало: <b>{money(shift.data.openingBalance)}</b></span>
                  <span>Приход: <b className="plus">{money(shift.data.income)}</b></span>
                  <span>Расход: <b className="minus">{money(shift.data.expense)}</b></span>
                </div>
                <button className="btn" onClick={() => setClosing(true)}>Закрыть смену</button>
              </div>
            ) : (
              <div className="shift-info">
                <span className="badge">Смена закрыта</span>
                <p className="muted">Чтобы проводить операции, откройте смену. Остаток на начало будет {money(register.balance, register.currency)}.</p>
                <button className="btn btn-primary" disabled={openShift.busy || shift.loading} onClick={openShift.submit}>Открыть смену</button>
              </div>
            )}
          </div>
          {shift.data && directories.data && (
            <OperationForm register={register} categories={directories.data[0]} counterparties={directories.data[1]}
              onDone={async (op) => { await refresh(); setPrinting(op) }} />
          )}
        </div>
      )}

      <h2 className="section-title">Операции за сегодня</h2>
      <Alert error={ops.error} />
      {ops.data && ops.data.length === 0 && <Empty>Сегодня операций ещё не было.</Empty>}
      {ops.data && ops.data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Время</th><th>Номер</th><th>Вид</th><th>Статья / основание</th><th>Контрагент</th>
                <th className="num">Сумма</th><th className="num">Остаток</th><th>Кассир</th><th></th>
              </tr>
            </thead>
            <tbody>
              {ops.data.map((o) => (
                <tr key={o.id} className={o.isReversed ? 'reversed' : ''}>
                  <td>{time(o.createdAt)}</td>
                  <td className="nowrap">{o.number}</td>
                  <td>{kindNames[o.kind]}{o.isReversed && <span className="badge badge-warn">отменена</span>}</td>
                  <td>{o.category}{o.description && <div className="muted small">{o.description}</div>}</td>
                  <td>{o.counterparty}</td>
                  <td className={`num ${o.delta > 0 ? 'plus' : 'minus'}`}>{o.delta > 0 ? '+' : '−'}{money(o.amount)}</td>
                  <td className="num">{money(o.balanceAfter)}</td>
                  <td className="small">{o.createdBy}</td>
                  <td className="nowrap">
                    {(o.kind === 'Income' || o.kind === 'Expense') && (
                      <button className="btn btn-small" onClick={() => setPrinting(o)}>Печать</button>
                    )}
                    {(o.kind === 'Income' || o.kind === 'Expense') && !o.isReversed && shift.data && (
                      <button className="btn btn-small btn-danger" onClick={() => setReversing(o)}>Отменить</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {closing && shift.data && <CloseShift register={register} shift={shift.data} onClose={() => setClosing(false)} onDone={refresh} />}
      {reversing && <Reverse op={reversing} onClose={() => setReversing(null)} onDone={refresh} />}
      {printing && <OrderPrint op={printing} register={register} onClose={() => setPrinting(null)} />}
    </>
  )
}

function OperationForm({ register, categories, counterparties, onDone }) {
  const empty = { amount: '', categoryId: '', counterpartyId: '', description: '' }
  const [kind, setKind] = useState('Income')
  const [form, setForm] = useState(empty)
  const list = categories.filter((c) => c.isActive && c.kind === kind)
  useEffect(() => setForm((f) => ({ ...f, categoryId: '' })), [kind])

  const { busy, error, submit, setError } = useSubmit(async () => {
    const amount = parseAmount(form.amount)
    if (amount === null) return setError(new Error('Введите сумму больше нуля, не больше двух знаков после запятой.'))
    if (!form.categoryId) return setError(new Error('Выберите статью.'))
    const op = await api('/operations', {
      method: 'POST',
      body: {
        cashRegisterId: register.id, kind, amount, categoryId: Number(form.categoryId),
        counterpartyId: form.counterpartyId ? Number(form.counterpartyId) : null, description: form.description,
      },
    })
    setForm(empty)
    await onDone(op)
  })
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value })

  return (
    <form className="card op-form" onSubmit={submit}>
      <div className="segmented" role="tablist">
        <button type="button" className={kind === 'Income' ? 'on plus-bg' : ''} onClick={() => setKind('Income')}>Приход (ПКО)</button>
        <button type="button" className={kind === 'Expense' ? 'on minus-bg' : ''} onClick={() => setKind('Expense')}>Расход (РКО)</button>
      </div>
      <Alert error={error} />
      <div className="grid-2">
        <Field label={`Сумма, ${register.currency}`}>
          <input inputMode="decimal" value={form.amount} onChange={set('amount')} placeholder="0,00" required />
        </Field>
        <Field label="Статья">
          <select value={form.categoryId} onChange={set('categoryId')} required>
            <option value="">— выберите —</option>
            {list.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </Field>
      </div>
      <Field label={kind === 'Income' ? 'Принято от' : 'Выдано'}>
        <select value={form.counterpartyId} onChange={set('counterpartyId')}>
          <option value="">— не указан —</option>
          {counterparties.filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </Field>
      <Field label="Основание">
        <input value={form.description} onChange={set('description')} maxLength={500} placeholder="Например: оплата по счёту № 15" />
      </Field>
      <button className={`btn ${kind === 'Income' ? 'btn-primary' : 'btn-expense'}`} disabled={busy}>
        {busy ? 'Проводим…' : kind === 'Income' ? 'Провести приход' : 'Провести расход'}
      </button>
    </form>
  )
}

function CloseShift({ register, shift, onClose, onDone }) {
  const [counted, setCounted] = useState('')
  const countedValue = counted === '' ? null : parseAmount(counted) ?? (counted.trim() === '0' ? 0 : NaN)
  const diff = countedValue === null || Number.isNaN(countedValue) ? null : countedValue - register.balance
  const { busy, error, submit, setError } = useSubmit(async () => {
    if (Number.isNaN(countedValue)) return setError(new Error('Неверная сумма пересчёта.'))
    await api(`/registers/${register.id}/shift/close`, { method: 'POST', body: { countedAmount: countedValue } })
    onClose() // окно закрываем до обновления: после закрытия смены её данных уже нет
    await onDone()
  })
  return (
    <Modal title="Закрытие смены" onClose={onClose}>
      <form onSubmit={submit}>
        <Alert error={error} />
        <div className="summary-list">
          <div><span>Остаток на начало</span><b>{money(shift.openingBalance, register.currency)}</b></div>
          <div><span>Приход за смену</span><b className="plus">{money(shift.income)}</b></div>
          <div><span>Расход за смену</span><b className="minus">{money(shift.expense)}</b></div>
          <div><span>Остаток по учёту</span><b>{money(register.balance, register.currency)}</b></div>
        </div>
        <Field label="Фактически в кассе (пересчёт)" hint="Можно оставить пустым, если пересчёт не делали.">
          <input inputMode="decimal" value={counted} onChange={(e) => setCounted(e.target.value)} placeholder={money(register.balance)} />
        </Field>
        {diff !== null && diff !== 0 && (
          <Alert kind="warn">{diff > 0 ? 'Излишек' : 'Недостача'}: {money(Math.abs(diff), register.currency)}</Alert>
        )}
        <div className="row-actions">
          <button className="btn btn-primary" disabled={busy}>Закрыть смену</button>
          <button type="button" className="btn" onClick={onClose}>Отмена</button>
        </div>
      </form>
    </Modal>
  )
}

function Reverse({ op, onClose, onDone }) {
  const [reason, setReason] = useState('')
  const { busy, error, submit } = useSubmit(async () => {
    await api(`/operations/${op.id}/reverse`, { method: 'POST', body: { reason } })
    await onDone()
    onClose()
  })
  return (
    <Modal title={`Отмена ${op.number}`} onClose={onClose}>
      <form onSubmit={submit}>
        <p>Операция не удаляется: будет проведена запись «сторно» на {money(op.amount)}, и остаток вернётся к прежнему.</p>
        <Alert error={error} />
        <Field label="Причина отмены">
          <input autoFocus value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} required />
        </Field>
        <div className="row-actions">
          <button className="btn btn-danger" disabled={busy}>Провести сторно</button>
          <button type="button" className="btn" onClick={onClose}>Не отменять</button>
        </div>
      </form>
    </Modal>
  )
}
