import React, {useState} from 'react';
import {HandCoins, Plus, Trash2, FileCheck2, Lock, AlarmClock, Download} from 'lucide-react';
import {api} from '../services/api.js';
import {advances as advanceApi, listItems, listTotal} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, DataTable, EmptyState, ErrorState, StatusPill, Amount, Drawer, SkeletonBlock, Field} from '../components/ui';
import {Attachments} from '../components/Attachments.jsx';
import {money, dateText, today, normalizeAmount} from '../utils/format.js';
import {advanceTotals, absAmount, toCents} from '../utils/money.js';
import {can} from '../utils/permissions.js';

const statusView = {issued: ['info', 'Выдан'], reported: ['warning', 'Отчёт принят'], closed: ['success', 'Закрыт']};
const isOverdue = a => a.overdue ?? (a.status === 'issued' && a.dueDate < today());
export function AdvanceStatus({advance}) {
  if (isOverdue(advance)) return <StatusPill tone="danger" icon={AlarmClock}>Просрочен</StatusPill>;
  const [tone, label] = statusView[advance.status] || ['neutral', advance.status];
  return <StatusPill tone={tone}>{label}</StatusPill>;
}
const plusDays = n => { const d = new Date(today() + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };

/** Итог «Израсходовано / Остаток / Перерасход». */
function Totals({amount, lines, currency}) {
  const t = advanceTotals(amount, lines);
  return <div className={'denom-summary ' + (t.overspent ? 'shortage' : t.settlement === 'none' ? 'match' : '')} aria-live="polite">
    <div><span>Выдано</span><span>{money(amount)} {currency}</span></div>
    <div><span>Израсходовано</span><span>{money(t.spent)} {currency}</span></div>
    <div className="total"><span>{t.overspent ? 'Перерасход (вернуть сотруднику)' : t.settlement === 'none' ? 'Остатка нет' : 'Остаток (сотрудник возвращает)'}</span><span>{money(absAmount(t.remainder))} {currency}</span></div>
  </div>;
}

function ReportForm({advance, categories, currency, onDone}) {
  const [lines, setLines] = useState([{date: today(), description: '', amount: '', categoryId: ''}]);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const set = (i, key, value) => setLines(ls => ls.map((l, j) => j === i ? {...l, [key]: value} : l));
  const normalized = lines.map(l => { try { return {...l, amount: normalizeAmount(l.amount)}; } catch { return {...l, amount: ''}; } });
  async function submit(e) {
    e.preventDefault(); setError('');
    for (const [i, l] of lines.entries()) {
      if (!l.description.trim() || !l.categoryId || !l.date) { setError(`Строка ${i + 1}: заполните дату, описание и категорию.`); return; }
      try { normalizeAmount(l.amount); } catch (err) { setError(`Строка ${i + 1}: ${err.message}`); return; }
    }
    setBusy(true);
    try { await advanceApi.report(advance.id, normalized.map(l => ({date: l.date, description: l.description.trim(), amount: l.amount, categoryId: Number(l.categoryId)}))); onDone('Авансовый отчёт принят.'); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  return <form onSubmit={submit} className="ui-stack" noValidate>
    <h3>Авансовый отчёт</h3>
    {lines.map((l, i) => <fieldset key={i} className="advance-line"><legend>Строка {i + 1}</legend>
      <div className="form-row"><Field label="Дата *"><input type="date" value={l.date} max={today()} onChange={e => set(i, 'date', e.target.value)}/></Field><Field label={'Сумма, ' + currency + ' *'}><input inputMode="decimal" value={l.amount} placeholder="0,00" onChange={e => set(i, 'amount', e.target.value)}/></Field></div>
      <Field label="Описание и номер чека *"><input value={l.description} maxLength={300} onChange={e => set(i, 'description', e.target.value)}/></Field>
      <div className="shift-line"><Field label="Категория расхода *"><select value={l.categoryId} onChange={e => set(i, 'categoryId', e.target.value)}><option value="">Выберите</option>{categories.filter(c => c.type === 'expense' && c.active !== false && !c.isSystem).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
        {lines.length > 1 && <button type="button" className="icon-btn" aria-label={`Удалить строку ${i + 1}`} onClick={() => setLines(ls => ls.filter((_, j) => j !== i))}><Trash2 size={18}/></button>}</div>
    </fieldset>)}
    <Button variant="ghost" icon={Plus} onClick={() => setLines(ls => [...ls, {date: today(), description: '', amount: '', categoryId: ''}])}>Добавить строку</Button>
    <Totals amount={advance.amount} lines={normalized} currency={currency}/>
    <p className="field-hint">Сканы чеков прикрепите к авансу ниже: после принятия отчёта изменить строки нельзя.</p>
    {error && <div className="error" role="alert">{error}</div>}
    <div className="row-actions"><Button type="submit" icon={FileCheck2} loading={busy}>Принять отчёт</Button></div>
  </form>;
}

function SettleForm({advance, currency, onDone}) {
  const remainder = advance.remainder ?? advanceTotals(advance.amount, advance.lines).remainder;
  const sign = toCents(remainder) > 0n ? 'return' : toCents(remainder) < 0n ? 'reimburse' : 'none';
  const [operationId, setOperationId] = useState(''), [busy, setBusy] = useState(false), [error, setError] = useState('');
  async function submit(e) {
    e.preventDefault(); setError('');
    if (sign !== 'none' && !/^\d+$/.test(operationId)) { setError('Укажите номер проведённой операции.'); return; }
    setBusy(true);
    try { await advanceApi.settle(advance.id, sign === 'none' ? undefined : Number(operationId)); onDone('Аванс закрыт.'); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  return <form onSubmit={submit} className="ui-stack">
    <h3>Закрытие аванса</h3>
    {sign === 'none' ? <p>Остатка и перерасхода нет — аванс закрывается без кассовой операции.</p> : <>
      <p>{sign === 'return' ? <>Сотрудник возвращает остаток <strong>{money(remainder)} {currency}</strong>. Проведите ПКО от {advance.counterparty} на эту сумму и укажите его номер.</>
        : <>Перерасход <strong>{money(absAmount(remainder))} {currency}</strong>. Проведите РКО сотруднику {advance.counterparty} на эту сумму и укажите его номер.</>}</p>
      <Field label={(sign === 'return' ? 'ID проведённого ПКО' : 'ID проведённого РКО') + ' *'}><input inputMode="numeric" value={operationId} onChange={e => setOperationId(e.target.value.replace(/\D/g, ''))}/></Field>
    </>}
    {error && <div className="error" role="alert">{error}</div>}
    <div className="row-actions"><Button type="submit" icon={Lock} loading={busy}>Закрыть аванс</Button></div>
  </form>;
}

function AdvanceDrawer({id, user, categories, currency, onClose, onChanged}) {
  const item = useAsync(signal => advanceApi.get(id, signal), [id]);
  const a = item.data;
  const manage = can(user, 'advance.manage');
  const done = message => { onChanged(message); item.reload(); };
  return <Drawer title={'Аванс №' + id} subtitle={a ? `${a.counterparty} · ${a.purpose}` : undefined} onClose={onClose} width={640}>
    {item.status === 'error' ? <ErrorState error={item.error} onRetry={item.reload}/> : !a ? <SkeletonBlock rows={4}/> : <>
      <div className="shift-line"><AdvanceStatus advance={a}/><span className="muted">выдан {dateText(a.issuedAt)} по {a.documentNumber || 'РКО №' + a.operationId}, отчитаться до {dateText(a.dueDate)}</span></div>
      {a.status !== 'issued' && <><Totals amount={a.amount} lines={a.lines || []} currency={currency}/>
        <table className="ui-table"><caption className="sr-only">Строки авансового отчёта</caption><thead><tr><th>Дата</th><th>Описание</th><th className="numeric">Сумма</th></tr></thead><tbody>{(a.lines || []).map(l => <tr key={l.id || l.description}><td>{dateText(l.date)}</td><td><strong>{l.description}</strong><small>{l.category}</small></td><td className="numeric">{money(l.amount)}</td></tr>)}</tbody></table></>}
      {a.status === 'issued' && manage && <ReportForm advance={a} categories={categories} currency={currency} onDone={done}/>}
      {a.status === 'issued' && !manage && <p className="muted">Ожидается авансовый отчёт сотрудника. Принимает отчёт бухгалтер.</p>}
      {a.status === 'reported' && manage && <SettleForm advance={a} currency={currency} onDone={done}/>}
      {a.status === 'closed' && <p className="muted">Аванс закрыт{a.settleOperationId ? ` операцией №${a.settleOperationId}` : ''}.</p>}
      <Attachments ownerType="advance" ownerId={a.id} canManage={manage && a.status === 'issued'} title="Чеки и подтверждающие документы"/>
    </>}
  </Drawer>;
}

function CreateDrawer({user, boxes, parties, currency, onClose, onDone}) {
  const employees = parties.filter(p => p.kind === 'employee');
  const ops = useAsync(async signal => {
    const from = plusDays(-30);
    const pages = await Promise.all(boxes.map(b => api('/operations?' + new URLSearchParams({cashboxId: b.id, from, to: today(), type: 'expense', status: 'posted', page: 1, pageSize: 50}), {signal}).catch(() => ({items: []}))));
    return pages.flatMap(listItems).filter(o => employees.some(e => e.id === o.counterpartyId || e.name === o.counterparty));
  }, []);
  const [form, setForm] = useState({operationId: '', purpose: '', dueDate: plusDays(7)}), [busy, setBusy] = useState(false), [error, setError] = useState('');
  async function submit(e) {
    e.preventDefault(); setError('');
    if (!form.operationId) { setError('Выберите проведённый РКО на сотрудника.'); return; }
    if (!form.purpose.trim()) { setError('Укажите цель аванса.'); return; }
    if (form.dueDate < today()) { setError('Срок отчёта не может быть в прошлом.'); return; }
    setBusy(true);
    try { await advanceApi.create({operationId: Number(form.operationId), purpose: form.purpose.trim(), dueDate: form.dueDate}); onDone('Аванс зарегистрирован.'); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  return <Drawer title="Новый аванс" subtitle="Аванс оформляется на основании проведённого РКО на подотчётное лицо" onClose={onClose} busy={busy}
    footer={<><Button variant="secondary" onClick={onClose}>Отмена</Button><Button type="submit" form="advance-form" loading={busy} disabled={!can(user, 'advance.manage')}>Создать аванс</Button></>}>
    <form id="advance-form" onSubmit={submit} className="ui-stack" noValidate>
      <Field label="Проведённый РКО на сотрудника *">{ops.status === 'loading' ? <SkeletonBlock rows={1}/> :
        <select value={form.operationId} onChange={e => setForm({...form, operationId: e.target.value})}><option value="">{ops.data?.length ? 'Выберите документ' : 'Нет РКО на сотрудников за 30 дней'}</option>{(ops.data || []).map(o => <option key={o.id} value={o.id}>{o.documentNumber || '№' + o.id} · {dateText(o.date)} · {o.counterparty} · {money(o.amount)} {currency}</option>)}</select>}</Field>
      <Field label="Цель аванса *"><input value={form.purpose} maxLength={300} onChange={e => setForm({...form, purpose: e.target.value})} placeholder="Например, командировка в Худжанд"/></Field>
      <Field label="Срок авансового отчёта *"><input type="date" min={today()} value={form.dueDate} onChange={e => setForm({...form, dueDate: e.target.value})}/></Field>
      {!employees.length && <p className="ui-callout tone-info">В справочнике нет контрагентов вида «Сотрудник». Добавьте подотчётное лицо в «Контрагентах».</p>}
      {error && <div className="error" role="alert">{error}</div>}
    </form>
  </Drawer>;
}

/** Авансы подотчётным лицам (контракт п.3). */
export function AdvancesPage({user, boxes = [], parties = [], categories = [], currency = 'TJS', filter = '', onFilter, onNotice}) {
  const [status, setStatus] = useState(['issued', 'reported', 'closed'].includes(filter) ? filter : ''), [overdue, setOverdue] = useState(filter === 'overdue');
  const [party, setParty] = useState(''), [index, setIndex] = useState(1), [open, setOpen] = useState(null), [creating, setCreating] = useState(false);
  const list = useAsync(signal => advanceApi.list({status, counterpartyId: party, overdue, page: index, pageSize: 15}, signal), [status, party, overdue, index]);
  const changed = message => { onNotice?.(message); list.reload(); };
  const columns = [
    {key: 'id', title: '№ и сотрудник', sortable: true, render: a => <><strong>№{a.id} · {a.counterparty}</strong><small>{a.purpose}</small></>},
    {key: 'issuedAt', title: 'Выдан', sortable: true, render: a => <>{dateText(a.issuedAt)}<small>{a.documentNumber || 'РКО №' + a.operationId}</small></>},
    {key: 'dueDate', title: 'Срок отчёта', sortable: true, render: a => <span className={isOverdue(a) ? 'text-danger' : undefined}>{dateText(a.dueDate)}</span>},
    {key: 'status', title: 'Статус', render: a => <AdvanceStatus advance={a}/>},
    {key: 'amount', title: 'Сумма, ' + currency, align: 'right', sortable: true, sortValue: a => toCents(a.amount), render: a => <Amount value={a.amount}/>},
    {key: 'remainder', title: 'Остаток', align: 'right', render: a => a.status === 'issued' ? <span className="muted">—</span> : toCents(a.remainder ?? '0') < 0n ? <span className="text-danger">перерасход {money(absAmount(a.remainder))}</span> : money(a.remainder)}
  ];
  return <div className="ui-stack">
    <div className="ui-toolbar">
      <label className="field"><span>Статус</span><select value={overdue ? 'overdue' : status} onChange={e => { const v = e.target.value; setOverdue(v === 'overdue'); setStatus(v === 'overdue' ? '' : v); setIndex(1); onFilter?.(v); }}><option value="">Все</option><option value="issued">Выданы</option><option value="overdue">Просроченные</option><option value="reported">Отчёт принят</option><option value="closed">Закрыты</option></select></label>
      <label className="field"><span>Подотчётное лицо</span><select value={party} onChange={e => { setParty(e.target.value); setIndex(1); }}><option value="">Все сотрудники</option>{parties.filter(p => p.kind === 'employee').map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      <div className="row-actions" style={{marginLeft: 'auto'}}>
        <Button variant="secondary" icon={Download} onClick={() => advanceApi.reportFile('csv', {status}).catch(e => onNotice?.(e.message))}>CSV</Button>
        <Button variant="secondary" icon={Download} onClick={() => advanceApi.reportFile('pdf', {status}).catch(e => onNotice?.(e.message))}>PDF</Button>
        {can(user, 'advance.manage') && <Button icon={Plus} onClick={() => setCreating(true)}>Новый аванс</Button>}
      </div>
    </div>
    <Card title="Авансы подотчётным лицам" subtitle="Просроченные — выданные авансы, по которым истёк срок отчёта (по времени Душанбе)">
      {list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload}/> :
        <DataTable loading={list.status === 'loading' && !list.data} columns={columns} rows={listItems(list.data)} page={index} total={listTotal(list.data)} onPage={setIndex} onRowClick={a => setOpen(a.id)} rowClassName={a => isOverdue(a) ? 'row-danger' : ''} caption="Авансы"
          empty={<EmptyState icon={HandCoins} title="Авансов нет" text={overdue ? 'Просроченных авансов нет — все сотрудники отчитались вовремя.' : 'Выданные под отчёт суммы появятся здесь.'} action={can(user, 'advance.manage') && <Button icon={Plus} onClick={() => setCreating(true)}>Новый аванс</Button>}/>}/>}
    </Card>
    {open && <AdvanceDrawer id={open} user={user} categories={categories} currency={currency} onClose={() => setOpen(null)} onChanged={changed}/>}
    {creating && <CreateDrawer user={user} boxes={boxes} parties={parties} currency={currency} onClose={() => setCreating(false)} onDone={message => { setCreating(false); changed(message); }}/>}
  </div>;
}
