import { useState } from 'react'
import { api } from '../lib/api.js'
import { dateTime, money, parseAmount } from '../lib/format.js'
import { Alert, Empty, Field, PageHead, useLoad, useSubmit } from '../components/ui.jsx'
import { useAuth } from '../auth.jsx'
import { can } from '../lib/permissions.js'

export default function Transfers() {
  const { user } = useAuth()
  const list = useLoad(() => api('/transfers'), [])
  return (
    <>
      <PageHead title="Переводы между кассами" />
      {can.operate(user) && <TransferForm onDone={list.reload} />}
      <h2 className="section-title">Последние переводы</h2>
      <Alert error={list.error} />
      {list.data?.length === 0 && <Empty>Переводов ещё не было.</Empty>}
      {list.data?.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Дата</th><th>Откуда</th><th>Куда</th><th className="num">Сумма</th><th>Основание</th><th>Кто</th></tr></thead>
            <tbody>
              {list.data.map((t) => (
                <tr key={t.id}>
                  <td className="nowrap">{dateTime(t.createdAt)}</td><td>{t.from}</td><td>{t.to}</td>
                  <td className="num">{money(t.amount, t.currency)}</td><td>{t.description}</td><td className="small">{t.createdBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

function TransferForm({ onDone }) {
  // Источник — только свои кассы, получатель — любая касса той же валюты (список с сервера для кассира ограничен).
  const registers = useLoad(() => api('/registers'), [])
  const [form, setForm] = useState({ from: '', to: '', amount: '', description: '' })
  const [ok, setOk] = useState(null)
  const all = (registers.data || []).filter((r) => r.isActive)
  const from = all.find((r) => r.id === Number(form.from))
  const targets = all.filter((r) => from && r.id !== from.id && r.currency === from.currency)
  const { busy, error, submit, setError } = useSubmit(async () => {
    const amount = parseAmount(form.amount)
    if (!from || !form.to) return setError(new Error('Выберите обе кассы.'))
    if (amount === null) return setError(new Error('Неверная сумма.'))
    const res = await api('/transfers', {
      method: 'POST', body: { fromRegisterId: from.id, toRegisterId: Number(form.to), amount, description: form.description },
    })
    setOk(`Перевод проведён: ${res.outNumber} и ${res.inNumber}.`)
    setForm({ from: form.from, to: '', amount: '', description: '' })
    await Promise.all([registers.reload(), onDone()])
  })
  const set = (k) => (e) => { setOk(null); setForm({ ...form, [k]: e.target.value }) }
  return (
    <form className="card" onSubmit={submit}>
      <Alert error={error || registers.error} />
      {ok && <Alert kind="ok">{ok}</Alert>}
      <div className="grid-4">
        <Field label="Из кассы">
          <select value={form.from} onChange={(e) => { setOk(null); setForm({ ...form, from: e.target.value, to: '' }) }}>
            <option value="">— выберите —</option>
            {all.map((r) => <option key={r.id} value={r.id}>{r.branchName} / {r.name} — {money(r.balance, r.currency)}</option>)}
          </select>
        </Field>
        <Field label="В кассу" hint={from && targets.length === 0 ? 'Нет доступной кассы в той же валюте.' : 'Только та же валюта, смена должна быть открыта.'}>
          <select value={form.to} onChange={set('to')} disabled={!from}>
            <option value="">— выберите —</option>
            {targets.map((r) => <option key={r.id} value={r.id}>{r.branchName} / {r.name}</option>)}
          </select>
        </Field>
        <Field label={`Сумма${from ? ', ' + from.currency : ''}`}>
          <input inputMode="decimal" value={form.amount} onChange={set('amount')} placeholder="0,00" />
        </Field>
        <Field label="Основание">
          <input value={form.description} onChange={set('description')} maxLength={500} />
        </Field>
      </div>
      <button className="btn btn-primary" disabled={busy}>Провести перевод</button>
    </form>
  )
}
