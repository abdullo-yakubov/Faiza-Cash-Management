import React, {useEffect, useState} from 'react';
import {ClipboardList, Plus, Trash2, FileDown, AlertTriangle, CheckCircle2, TrendingUp} from 'lucide-react';
import {api} from '../services/api.js';
import {inventories, denominations as denominationApi, listItems} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, DataTable, EmptyState, ErrorState, StatusPill, Drawer, SkeletonBlock, Field} from '../components/ui';
import {DenominationCalculator, emptyCounts, filledRows} from '../components/DenominationCalculator.jsx';
import {money, today, dateTimeText} from '../utils/format.js';
import {denominationTotal, reconcile, absAmount, toCents} from '../utils/money.js';
import {can} from '../utils/permissions.js';

function DiffPill({difference}) {
  const c = toCents(difference ?? '0');
  if (c === 0n) return <StatusPill tone="success" icon={CheckCircle2}>Совпадает</StatusPill>;
  return c < 0n ? <StatusPill tone="danger" icon={AlertTriangle}>Недостача {money(absAmount(difference))}</StatusPill> : <StatusPill tone="warning" icon={TrendingUp}>Излишек {money(difference)}</StatusPill>;
}

function ActDrawer({boxes, box, currency, user, onClose, onDone}) {
  const [cashboxId, setCashboxId] = useState(box || String(boxes[0]?.id || ''));
  const list = useAsync(signal => denominationApi.list(currency, signal), [currency]);
  const balance = useAsync(signal => cashboxId ? api(`/cashboxes/${cashboxId}/balance?date=${today()}`, {signal}) : null, [cashboxId]);
  const [rows, setRows] = useState(null), [commission, setCommission] = useState([user.fullName, '']), [note, setNote] = useState('');
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  useEffect(() => { if (list.status === 'success' && !rows) setRows(emptyCounts(list.data || [])); }, [list.status, list.data, rows]);
  async function submit() {
    setError('');
    const members = commission.map(x => x.trim()).filter(Boolean);
    if (members.length < 2) { setError('В комиссии должно быть не меньше двух человек.'); return; }
    if (denominationTotal(rows).invalid.length) { setError('Исправьте количество купюр.'); return; }
    const expected = balance.data?.balance;
    const kind = expected != null ? reconcile(denominationTotal(rows).total, expected).kind : 'match';
    if (!window.confirm(`Акт неизменяем после проведения.${kind !== 'match' ? ' Обнаружено расхождение с расчётным остатком.' : ''} Провести акт инвентаризации?`)) return;
    setBusy(true);
    try { const act = await inventories.create({cashboxId: Number(cashboxId), denominations: filledRows(rows), commission: members, note: note.trim()}); onDone(`Акт инвентаризации №${act.id} проведён.`); }
    catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  return <Drawer title="Акт инвентаризации кассы" subtitle="Пересчёт наличных комиссией. Расчётный остаток на момент акта определит сервер." onClose={onClose} busy={busy} width={620}
    footer={<><Button variant="secondary" onClick={onClose} disabled={busy}>Отмена</Button><Button icon={ClipboardList} loading={busy} disabled={!rows} onClick={submit}>Провести акт</Button></>}>
    <Field label="Касса *"><select value={cashboxId} onChange={e => setCashboxId(e.target.value)}>{boxes.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></Field>
    {list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload}/> : rows ? <DenominationCalculator rows={rows} onChange={setRows} expected={balance.data?.balance} currency={currency} disabled={busy}/> : <SkeletonBlock rows={4}/>}
    <fieldset className="roles-field"><legend>Состав комиссии *</legend>
      {commission.map((name, i) => <div className="shift-line" key={i}><input aria-label={`Член комиссии ${i + 1}`} value={name} maxLength={160} placeholder="Фамилия И. О." onChange={e => setCommission(c => c.map((x, j) => j === i ? e.target.value : x))} style={{flex: 1}}/>
        {commission.length > 2 && <button type="button" className="icon-btn" aria-label={`Удалить члена комиссии ${i + 1}`} onClick={() => setCommission(c => c.filter((_, j) => j !== i))}><Trash2 size={18}/></button>}</div>)}
      <Button variant="ghost" icon={Plus} onClick={() => setCommission(c => [...c, ''])}>Добавить члена комиссии</Button>
    </fieldset>
    <Field label="Примечание"><textarea value={note} maxLength={500} onChange={e => setNote(e.target.value)}/></Field>
    {error && <div className="error" role="alert">{error}</div>}
  </Drawer>;
}

/** Инвентаризация кассы (контракт п.4): проведение акта и история. Акты неизменяемы. */
export function InventoryPage({user, boxes = [], box, currency = 'TJS', onNotice}) {
  const [filter, setFilter] = useState(''), [creating, setCreating] = useState(false), [open, setOpen] = useState(null);
  const list = useAsync(signal => inventories.list(filter, signal), [filter]);
  const download = item => inventories.act(item).catch(e => onNotice?.(e.message));
  const columns = [
    {key: 'createdAt', title: 'Акт', sortable: true, render: x => <><strong>№{x.id}</strong><small>{dateTimeText(x.createdAt)}</small></>},
    {key: 'cashbox', title: 'Касса', sortable: true, render: x => x.cashbox || boxes.find(b => b.id === x.cashboxId)?.name || 'Касса №' + x.cashboxId},
    {key: 'commission', title: 'Комиссия', render: x => <small>{(x.commission || []).join(', ')}</small>},
    {key: 'actualBalance', title: 'Факт / расчёт, ' + currency, align: 'right', render: x => <>{money(x.actualBalance)}<small>расчёт {money(x.expectedBalance)}</small></>},
    {key: 'difference', title: 'Результат', render: x => <DiffPill difference={x.difference}/>},
    {key: 'pdf', title: <span className="sr-only">Акт PDF</span>, render: x => <button type="button" className="icon-btn" aria-label={`Скачать акт №${x.id} в PDF`} title="Скачать PDF" onClick={e => { e.stopPropagation(); download(x); }}><FileDown size={18}/></button>}
  ];
  return <div className="ui-stack">
    <div className="ui-toolbar">
      <label className="field"><span>Касса</span><select value={filter} onChange={e => setFilter(e.target.value)}><option value="">Все доступные</option>{boxes.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
      {can(user, 'inventory.create') && <div style={{marginLeft: 'auto'}}><Button icon={Plus} onClick={() => setCreating(true)}>Провести инвентаризацию</Button></div>}
    </div>
    <Card title="История актов" subtitle="Акт после проведения не изменяется и не удаляется (как журнал аудита)">
      {list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload}/> :
        <DataTable loading={list.status === 'loading' && !list.data} columns={columns} rows={listItems(list.data)} onRowClick={setOpen} caption="Акты инвентаризации" initialSort={{key: 'createdAt', dir: 'desc'}}
          empty={<EmptyState icon={ClipboardList} title="Актов пока нет" text="Проведите первую инвентаризацию: пересчитайте наличные по купюрам вместе с комиссией." action={can(user, 'inventory.create') && <Button icon={Plus} onClick={() => setCreating(true)}>Провести инвентаризацию</Button>}/>}/>}
    </Card>
    {open && <Drawer title={'Акт инвентаризации №' + open.id} subtitle={dateTimeText(open.createdAt) + ' · ' + (open.createdBy || '')} onClose={() => setOpen(null)} footer={<Button variant="secondary" icon={FileDown} onClick={() => download(open)}>Скачать PDF</Button>}>
      <DiffPill difference={open.difference}/>
      <table className="ui-table"><caption className="sr-only">Пересчёт по купюрам</caption><thead><tr><th>Номинал</th><th className="numeric">Количество</th><th className="numeric">Сумма</th></tr></thead>
        <tbody>{denominationTotal(open.denominations || []).lines.map(l => <tr key={l.value}><td>{money(l.value)}</td><td className="numeric">{l.count}</td><td className="numeric">{money(l.amount)}</td></tr>)}</tbody></table>
      <div className="denom-summary"><div className="total"><span>Факт</span><span>{money(open.actualBalance)} {currency}</span></div><div><span>Расчётный остаток</span><span>{money(open.expectedBalance)} {currency}</span></div></div>
      <p><strong>Комиссия:</strong> {(open.commission || []).join(', ')}</p>
      {open.note && <p><strong>Примечание:</strong> {open.note}</p>}
    </Drawer>}
    {creating && <ActDrawer boxes={boxes.filter(b => b.active !== false)} box={box} currency={currency} user={user} onClose={() => setCreating(false)} onDone={m => { setCreating(false); onNotice?.(m); list.reload(); }}/>}
  </div>;
}
