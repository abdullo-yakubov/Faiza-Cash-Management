import { useState } from 'react'
import { api, download } from '../lib/api.js'
import { dateTime, kindNames, money, today } from '../lib/format.js'
import { Alert, Empty, Field, PageHead, useLoad, useSubmit } from '../components/ui.jsx'

export default function Reports() {
  const [tab, setTab] = useState('cashbook')
  return (
    <>
      <PageHead title="Отчёты">
        <div className="segmented no-print">
          <button className={tab === 'cashbook' ? 'on' : ''} onClick={() => setTab('cashbook')}>Кассовая книга</button>
          <button className={tab === 'summary' ? 'on' : ''} onClick={() => setTab('summary')}>Сводка по статьям</button>
          <button className={tab === 'shifts' ? 'on' : ''} onClick={() => setTab('shifts')}>Смены</button>
        </div>
      </PageHead>
      {tab === 'cashbook' && <CashBook />}
      {tab === 'summary' && <Summary />}
      {tab === 'shifts' && <Shifts />}
    </>
  )
}

function useRegisters() {
  return useLoad(() => api('/registers'), [])
}

function CashBook() {
  const registers = useRegisters()
  const [filter, setFilter] = useState({ registerId: '', from: today(), to: today() })
  const [book, setBook] = useState(null)
  const registerId = filter.registerId || registers.data?.[0]?.id
  const query = `registerId=${registerId}&from=${filter.from}&to=${filter.to}`
  const load = useSubmit(async () => setBook(await api(`/reports/cashbook?${query}`)))
  const csv = useSubmit(() => download(`/reports/cashbook.csv?${query}`, 'cashbook.csv'))

  return (
    <>
      <form className="card filters no-print" onSubmit={load.submit}>
        <Field label="Касса">
          <select value={registerId || ''} onChange={(e) => setFilter({ ...filter, registerId: e.target.value })}>
            {(registers.data || []).map((r) => <option key={r.id} value={r.id}>{r.branchName} / {r.name} ({r.currency})</option>)}
          </select>
        </Field>
        <Field label="С"><input type="date" value={filter.from} onChange={(e) => setFilter({ ...filter, from: e.target.value })} /></Field>
        <Field label="По"><input type="date" value={filter.to} onChange={(e) => setFilter({ ...filter, to: e.target.value })} /></Field>
        <div className="row-actions">
          <button className="btn btn-primary" disabled={load.busy || !registerId}>Сформировать</button>
          {book && <button type="button" className="btn" onClick={csv.submit}>Excel (CSV)</button>}
          {book && <button type="button" className="btn" onClick={() => window.print()}>Печать / PDF</button>}
        </div>
      </form>
      <Alert error={load.error || csv.error || registers.error} />
      {book && (
        <div className="card print-area">
          <h2>Кассовая книга</h2>
          <p className="muted">
            ООО «Фаиза» · {book.branch} / {book.register} · {new Date(book.from).toLocaleDateString('ru-RU')} — {new Date(book.to).toLocaleDateString('ru-RU')} · {book.currency}
          </p>
          <div className="summary-list horizontal">
            <div><span>Остаток на начало</span><b>{money(book.openingBalance)}</b></div>
            <div><span>Приход</span><b className="plus">{money(book.income)}</b></div>
            <div><span>Расход</span><b className="minus">{money(book.expense)}</b></div>
            <div><span>Остаток на конец</span><b>{money(book.closingBalance)}</b></div>
          </div>
          {book.operations.length === 0 ? <Empty>За период операций нет.</Empty> : (
            <div className="table-wrap">
              <table>
                <thead><tr><th>Дата</th><th>Номер</th><th>Вид</th><th>Статья / основание</th><th className="num">Приход</th><th className="num">Расход</th><th className="num">Остаток</th><th>Кассир</th></tr></thead>
                <tbody>
                  {book.operations.map((o) => (
                    <tr key={o.id}>
                      <td className="nowrap">{dateTime(o.createdAt)}</td><td className="nowrap">{o.number}</td><td>{kindNames[o.kind]}</td>
                      <td>{o.category}{o.counterparty && ` · ${o.counterparty}`}{o.description && <div className="muted small">{o.description}</div>}</td>
                      <td className="num plus">{o.delta > 0 ? money(o.delta) : ''}</td>
                      <td className="num minus">{o.delta < 0 ? money(-o.delta) : ''}</td>
                      <td className="num">{money(o.balanceAfter)}</td><td className="small">{o.createdBy}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </>
  )
}

function Summary() {
  const registers = useRegisters()
  const first = today().slice(0, 8) + '01'
  const [filter, setFilter] = useState({ registerId: '', from: first, to: today() })
  const [rows, setRows] = useState(null)
  const load = useSubmit(async () => {
    const reg = filter.registerId ? `&registerId=${filter.registerId}` : ''
    setRows(await api(`/reports/summary?from=${filter.from}&to=${filter.to}${reg}`))
  })
  const byCurrency = Object.groupBy ? Object.groupBy(rows || [], (r) => r.currency) : {}
  return (
    <>
      <form className="card filters no-print" onSubmit={load.submit}>
        <Field label="Касса">
          <select value={filter.registerId} onChange={(e) => setFilter({ ...filter, registerId: e.target.value })}>
            <option value="">Все доступные</option>
            {(registers.data || []).map((r) => <option key={r.id} value={r.id}>{r.branchName} / {r.name}</option>)}
          </select>
        </Field>
        <Field label="С"><input type="date" value={filter.from} onChange={(e) => setFilter({ ...filter, from: e.target.value })} /></Field>
        <Field label="По"><input type="date" value={filter.to} onChange={(e) => setFilter({ ...filter, to: e.target.value })} /></Field>
        <div className="row-actions">
          <button className="btn btn-primary" disabled={load.busy}>Сформировать</button>
          {rows && <button type="button" className="btn" onClick={() => window.print()}>Печать / PDF</button>}
        </div>
      </form>
      <Alert error={load.error} />
      {rows && rows.length === 0 && <Empty>За период операций по статьям нет.</Empty>}
      {Object.entries(byCurrency).map(([currency, list]) => {
        const income = list.filter((r) => r.kind === 'Income')
        const expense = list.filter((r) => r.kind === 'Expense')
        const sum = (l) => l.reduce((s, r) => s + r.total, 0)
        return (
          <div className="card print-area" key={currency}>
            <h2>Сводка по статьям, {currency}</h2>
            <div className="grid-2">
              {[['Приход', income, 'plus'], ['Расход', expense, 'minus']].map(([title, l, cls]) => (
                <div key={title}>
                  <h3>{title}: <span className={cls}>{money(sum(l))}</span></h3>
                  {l.map((r) => (
                    <div className="bar-row" key={r.category}>
                      <div className="bar-label"><span>{r.category}</span><b>{money(r.total)}</b></div>
                      <div className="bar"><div className={`bar-fill ${cls}-bg`} style={{ width: `${sum(l) ? (r.total / sum(l)) * 100 : 0}%` }} /></div>
                      <div className="muted small">{r.count} опер.</div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </div>
        )
      })}
    </>
  )
}

function Shifts() {
  const registers = useRegisters()
  const [registerId, setRegisterId] = useState('')
  const id = registerId || registers.data?.[0]?.id
  const shifts = useLoad(() => (id ? api(`/shifts?registerId=${id}`) : Promise.resolve([])), [id])
  return (
    <>
      <div className="card filters no-print">
        <Field label="Касса">
          <select value={id || ''} onChange={(e) => setRegisterId(e.target.value)}>
            {(registers.data || []).map((r) => <option key={r.id} value={r.id}>{r.branchName} / {r.name}</option>)}
          </select>
        </Field>
      </div>
      <Alert error={shifts.error} />
      {shifts.data?.length === 0 && <Empty>Смен ещё не было.</Empty>}
      {shifts.data?.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Открыта</th><th>Закрыта</th><th className="num">На начало</th><th className="num">Приход</th><th className="num">Расход</th><th className="num">На конец</th><th className="num">Пересчёт</th><th className="num">Расхождение</th></tr></thead>
            <tbody>
              {shifts.data.map((s) => (
                <tr key={s.id}>
                  <td>{dateTime(s.openedAt)}<div className="muted small">{s.openedBy}</div></td>
                  <td>{s.closedAt ? <>{dateTime(s.closedAt)}<div className="muted small">{s.closedBy}</div></> : <span className="badge badge-ok">открыта</span>}</td>
                  <td className="num">{money(s.openingBalance)}</td>
                  <td className="num plus">{money(s.income)}</td>
                  <td className="num minus">{money(s.expense)}</td>
                  <td className="num">{s.closingBalance != null ? money(s.closingBalance) : ''}</td>
                  <td className="num">{s.countedAmount != null ? money(s.countedAmount) : ''}</td>
                  <td className={`num ${s.difference < 0 ? 'minus' : s.difference > 0 ? 'plus' : ''}`}>{s.difference != null ? money(s.difference) : ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
