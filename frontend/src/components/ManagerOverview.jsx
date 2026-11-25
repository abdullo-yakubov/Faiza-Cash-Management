import React, {useMemo, useState} from 'react';
import {ResponsiveContainer, LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid} from 'recharts';
import {Wallet, ArrowDownLeft, ArrowUpRight, FileEdit, ClipboardCheck, AlarmClock, Scale, Table2, LineChart as LineIcon, Building2} from 'lucide-react';
import {Card, StatTile, EmptyState} from './ui';
import {money, dateText, today, monthStart} from '../utils/format.js';
import {toCents, fromCents, addAmounts} from '../utils/money.js';
import {routeHash} from '../utils/route.js';

const shortDate = iso => iso ? iso.slice(8, 10) + '.' + iso.slice(5, 7) : '';
// Ось Y: компактные подписи («25 тыс.»). Геометрия графика — числа, итоги — строки/BigInt.
const compact = value => new Intl.NumberFormat('ru-RU', {notation: 'compact', maximumFractionDigits: 1}).format(value);

function FlowTooltip({active, payload, label, currency}) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;
  return <div className="chart-tooltip" role="status">
    <strong>{dateText(label)}</strong>
    <div><i style={{background: 'var(--chart-income)'}}/>Приход <b>+ {money(row.incomeText)} {currency}</b></div>
    <div><i style={{background: 'var(--chart-expense)'}}/>Расход <b>− {money(row.expenseText)} {currency}</b></div>
    <div className="muted">Сальдо {money(fromCents(toCents(row.incomeText) - toCents(row.expenseText)))} {currency}</div>
  </div>;
}

/**
 * Верх dashboard руководителя (контракт п.7): KPI, движение денег за 30 дней,
 * остатки по филиалам, топ-5 категорий расходов.
 */
export function ManagerOverview({d, currency = 'TJS', draftCount, reduceMotion}) {
  const [asTable, setAsTable] = useState(false);
  const discrepancies = (d.attention || []).filter(x => x.code === 'shift_discrepancy').length;
  const series = useMemo(() => (d.cashflowDaily || []).map(x => ({date: x.date, income: Number(x.income), expense: Number(x.expense), incomeText: x.income, expenseText: x.expense})), [d.cashflowDaily]);
  const totals = useMemo(() => ({income: addAmounts((d.cashflowDaily || []).map(x => x.income)), expense: addAmounts((d.cashflowDaily || []).map(x => x.expense))}), [d.cashflowDaily]);
  const branchMax = (d.branchBalances || []).reduce((m, b) => { const c = toCents(b.balance); return c > m ? c : m; }, 0n);
  const top = (d.topExpenseCategories || []).slice(0, 5);
  const topMax = top.reduce((m, c) => { const v = toCents(c.amount); return v > m ? v : m; }, 0n);
  const share = (value, max) => max > 0n ? Number((toCents(value) * 1000n) / max) / 10 : 0;
  const last = series.at(-1);
  return <>
    <div className="ui-tiles manager-kpis">
      <StatTile wide label="Общий остаток" value={d.totalBalance} currency={currency} icon={Wallet} tone="accent" hint="по доступным кассам"/>
      <StatTile wide label="Приход сегодня" value={d.periodTotals?.todayIncome} currency={currency} tone="income"/>
      <StatTile wide label="Расход сегодня" value={d.periodTotals?.todayExpense} currency={currency} tone="expense"/>
      <StatTile label="Черновики" kind="count" value={draftCount ?? '—'} icon={FileEdit} tone={draftCount ? 'warning' : 'neutral'} hint="не влияют на остаток" href={routeHash({page: 'operations', status: 'draft', from: monthStart(), to: today()})}/>
      <StatTile label="Ждут согласования" kind="count" value={d.pendingApprovals ?? '—'} icon={ClipboardCheck} tone={d.pendingApprovals ? 'warning' : 'neutral'} href="#/approvals"/>
      <StatTile label="Просроченные авансы" kind="count" value={d.overdueAdvances ?? '—'} icon={AlarmClock} tone={d.overdueAdvances ? 'danger' : 'neutral'} href="#/advances?filter=overdue"/>
      <StatTile label="Расхождения смен" kind="count" value={discrepancies} icon={Scale} tone={discrepancies ? 'danger' : 'neutral'} hint="за выбранный период" href="#/shifts"/>
    </div>
    <div className="ui-grid-2">
      <Card title="Движение денег за 30 дней" subtitle={`Приход + ${money(totals.income)} · расход − ${money(totals.expense)} ${currency}. Без межкассовых переводов.`}
        actions={<button type="button" className="button secondary" aria-pressed={asTable} onClick={() => setAsTable(v => !v)}>{asTable ? <><LineIcon size={16}/>График</> : <><Table2 size={16}/>Таблица</>}</button>}>
        {!series.length ? <EmptyState icon={LineIcon} title="Нет данных" text="Движение появится после проведения первых документов."/> : asTable ?
          <div className="table-scroll" style={{maxHeight: 280}}><table className="ui-table"><caption className="sr-only">Приход и расход по дням</caption><thead><tr><th scope="col">Дата</th><th scope="col" className="numeric">Приход, {currency}</th><th scope="col" className="numeric">Расход, {currency}</th></tr></thead>
            <tbody>{[...series].reverse().map(r => <tr key={r.date}><td>{dateText(r.date)}</td><td className="numeric">+ {money(r.incomeText)}</td><td className="numeric">− {money(r.expenseText)}</td></tr>)}</tbody></table></div> :
          <>
            <div className="ui-legend" aria-hidden="true"><span><i style={{background: 'var(--chart-income)'}}/>Приход</span><span><i style={{background: 'var(--chart-expense)'}}/>Расход</span></div>
            <div className="ui-chart" role="img" aria-label={`График прихода и расхода за 30 дней. Всего приход ${money(totals.income)}, расход ${money(totals.expense)} ${currency}. Подробности — в режиме «Таблица».`}>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={series} margin={{top: 12, right: 56, bottom: 0, left: 0}}>
                  <CartesianGrid vertical={false} stroke="var(--chart-grid)"/>
                  <XAxis dataKey="date" tickFormatter={shortDate} tick={{fontSize: 12, fill: 'var(--muted)'}} axisLine={{stroke: 'var(--border)'}} tickLine={false} minTickGap={18}/>
                  <YAxis tickFormatter={compact} tick={{fontSize: 12, fill: 'var(--muted)'}} axisLine={false} tickLine={false} width={56}/>
                  <Tooltip content={<FlowTooltip currency={currency}/>} cursor={{stroke: 'var(--border-strong)', strokeWidth: 1}}/>
                  <Line type="linear" dataKey="income" name="Приход" stroke="var(--chart-income)" strokeWidth={2} dot={false} activeDot={{r: 5, strokeWidth: 2, stroke: 'var(--panel)'}} isAnimationActive={!reduceMotion}
                    label={({index, x, y}) => index === series.length - 1 ? <text key="li" x={x + 8} y={y} dy={4} fontSize={12} fill="var(--text)">Приход</text> : null}/>
                  <Line type="linear" dataKey="expense" name="Расход" stroke="var(--chart-expense)" strokeWidth={2} dot={false} activeDot={{r: 5, strokeWidth: 2, stroke: 'var(--panel)'}} isAnimationActive={!reduceMotion}
                    label={({index, x, y}) => index === series.length - 1 ? <text key="le" x={x + 8} y={y} dy={4} fontSize={12} fill="var(--text)">Расход</text> : null}/>
                </LineChart>
              </ResponsiveContainer>
            </div>
            {last && <p className="field-hint">Последний день ({dateText(last.date)}): приход + {money(last.incomeText)}, расход − {money(last.expenseText)} {currency}.</p>}
          </>}
      </Card>
      <div className="ui-stack">
        <Card title="Остатки по филиалам">
          {(d.branchBalances || []).length ? (d.branchBalances || []).map(b => <div className="ui-bar-row" key={b.branchId}>
            <span className="shift-line"><Building2 size={15} aria-hidden="true"/>{b.name}</span>
            <span className="ui-bar-track" aria-hidden="true"><span className="ui-bar-fill neutral" style={{width: share(b.balance, branchMax) + '%', display: 'block'}}/></span>
            <strong className="ui-amount">{money(b.balance)}</strong>
          </div>) : <p className="muted">Филиалы с кассами не найдены.</p>}
        </Card>
        <Card title="Топ-5 категорий расходов" subtitle="за выбранный период">
          {top.length ? top.map(c => <div className="ui-bar-row" key={c.categoryId}>
            <span>{c.name}</span>
            <span className="ui-bar-track" aria-hidden="true"><span className="ui-bar-fill" style={{width: share(c.amount, topMax) + '%', display: 'block'}}/></span>
            <strong className="ui-amount">− {money(c.amount)}</strong>
          </div>) : <p className="muted">Расходов за период нет.</p>}
        </Card>
      </div>
    </div>
  </>;
}
