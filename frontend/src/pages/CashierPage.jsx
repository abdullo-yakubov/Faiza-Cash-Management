import React, {useEffect, useState} from 'react';
import {ArrowDownLeft, ArrowUpRight, LockOpen, Lock, Receipt, Wallet, History, Calculator} from 'lucide-react';
import {api} from '../services/api.js';
import {shifts as shiftApi, denominations as denominationApi, listItems} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, StatusPill, EmptyState, ErrorState, SkeletonBlock, Skeleton, Drawer, Amount, OperationStatusPill, ApprovalPill} from '../components/ui';
import {DenominationCalculator, emptyCounts, filledRows} from '../components/DenominationCalculator.jsx';
import {money, today, dateText, normalizeAmount, dateTimeText} from '../utils/format.js';
import {denominationTotal, reconcile} from '../utils/money.js';

// Остаток может быть нулевым, а normalizeAmount требует сумму > 0.
const balanceInput = text => /^\s*0*([.,]0{0,2})?\s*$/.test(String(text)) ? '0.00' : normalizeAmount(text);
import {can} from '../utils/permissions.js';

const daysAgo = n => { const d = new Date(today() + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() - n); return d.toISOString().slice(0, 10); };

/** Закрытие смены с пересчётом по купюрам. */
function CloseShiftDrawer({shift, expected, currency, onClose, onDone}) {
  const list = useAsync(signal => denominationApi.list(currency, signal), [currency]);
  const [rows, setRows] = useState(null), [manual, setManual] = useState(false), [amount, setAmount] = useState('');
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  useEffect(() => { if (list.status === 'success' && !rows) setRows(emptyCounts(list.data || [])); }, [list.status, list.data, rows]);
  const total = rows ? denominationTotal(rows) : null;
  const useManual = manual || (list.status !== 'loading' && !(list.data || []).length);
  async function submit() {
    setError('');
    let closing;
    if (useManual) { try { closing = balanceInput(amount); } catch (e) { setError(e.message); return; } }
    else { if (total.invalid.length) { setError('Исправьте количество купюр.'); return; } closing = total.total; }
    const kind = reconcile(closing, expected ?? closing).kind;
    if (kind !== 'match' && !window.confirm(`Фактический остаток ${money(closing)} ${currency} не совпадает с расчётным ${money(expected)} ${currency}. Расхождение будет зафиксировано. Закрыть смену?`)) return;
    setBusy(true);
    try { await shiftApi.close(shift.id, closing, useManual ? undefined : filledRows(rows)); onDone('Смена закрыта. Пересчёт сохранён.'); }
    catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  return <Drawer title={'Закрытие смены №' + shift.id} subtitle={`Дата ${dateText(shift.date)}. Пересчитайте наличность в кассе.`} onClose={onClose} busy={busy}
    footer={<><Button variant="secondary" onClick={onClose} disabled={busy}>Отмена</Button><Button icon={Lock} loading={busy} onClick={submit} disabled={!useManual && !rows}>Закрыть смену</Button></>}>
    {list.status === 'error' && <ErrorState error={list.error} onRetry={list.reload} title="Справочник номиналов недоступен"/>}
    {!useManual && (rows ? <DenominationCalculator rows={rows} onChange={setRows} expected={expected} currency={currency} disabled={busy}/> : <SkeletonBlock rows={4}/>)}
    {useManual && <label className="field"><span>Фактический остаток, {currency}</span><input inputMode="decimal" value={amount} onChange={e => setAmount(e.target.value)} placeholder="Например, 12 500,00" autoFocus/>
      <span className="field-hint">Расчётный остаток: {money(expected)} {currency}</span></label>}
    {(list.data || []).length > 0 && <button type="button" className="text-btn" onClick={() => setManual(v => !v)}><Calculator size={16}/>{manual ? 'Пересчитать по купюрам' : 'Ввести итоговую сумму без пересчёта'}</button>}
    {error && <div className="error" role="alert">{error}</div>}
  </Drawer>;
}

export function CashierPage({user, boxes, box, onBox, currency = 'TJS', onOperation, onDetails, revision = 0, onNotice}) {
  const activeBoxes = boxes.filter(b => b.active);
  const boxId = activeBoxes.some(b => String(b.id) === box) ? box : String(activeBoxes[0]?.id || '');
  const [attempt, setAttempt] = useState(0), [closing, setClosing] = useState(false), [opening, setOpening] = useState(''), [busy, setBusy] = useState(false), [error, setError] = useState('');
  const data = useAsync(async signal => {
    if (!boxId) return null;
    const [shiftRows, balance, ops, receipts] = await Promise.all([
      shiftApi.list(boxId, signal),
      api(`/cashboxes/${boxId}/balance?date=${today()}`, {signal}),
      api('/operations?' + new URLSearchParams({cashboxId: boxId, from: daysAgo(30), to: today(), sort: 'date_desc', page: 1, pageSize: 10}), {signal}),
      can(user, 'receipt.accept') ? api('/representative-receipts', {signal}).catch(() => []) : Promise.resolve([])
    ]);
    return {shifts: listItems(shiftRows), balance, ops: listItems(ops), receipts: listItems(receipts).filter(r => r.status === 'handed_over' && (!r.cashboxId || String(r.cashboxId) === boxId))};
  }, [boxId, revision, attempt]);
  const reload = () => setAttempt(v => v + 1);
  const d = data.data;
  const current = d?.shifts.find(s => s.status === 'open');
  const canManage = can(user, 'shift.manage');
  const canCreate = can(user, 'operation.create');
  async function openShift(e) {
    e.preventDefault(); setError('');
    let value; try { value = String(opening).trim() === '' ? d.balance.balance : balanceInput(opening); } catch (err) { setError(err.message); return; }
    setBusy(true);
    try { await shiftApi.open({cashboxId: Number(boxId), date: today(), openingBalance: value}); setOpening(''); onNotice?.('Смена открыта. Можно проводить документы.'); reload(); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  async function accept(receipt) {
    setBusy(true); setError('');
    try { await api(`/representative-receipts/${receipt.id}/accept`, {method: 'POST'}); onNotice?.(`Квитанция №${receipt.id} принята в кассу.`); reload(); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  if (!activeBoxes.length) return <section className="panel"><EmptyState icon={Wallet} title="Нет доступных касс" text="Попросите администратора назначить вам доступ к кассе."/></section>;
  return <div className="ui-stack cashier-page">
    {activeBoxes.length > 1 && <div className="ui-toolbar"><label className="field"><span>Касса</span><select value={boxId} onChange={e => onBox(e.target.value)}>{activeBoxes.map(b => <option key={b.id} value={b.id}>{b.name}{b.branch ? ' · ' + b.branch : ''}</option>)}</select></label></div>}
    {error && <div className="error" role="alert">{error}</div>}
    {data.status === 'error' ? <section className="panel"><ErrorState error={data.error} onRetry={reload}/></section> : <>
      <div className="cashier-hero">
        <section className="cashier-balance" aria-labelledby="cashier-balance-title">
          <span className="label" id="cashier-balance-title">Остаток в кассе на {dateText(today())}</span>
          {!d ? <Skeleton height={44} width="70%"/> : <span className="value" aria-live="polite">{money(d.balance.balance)} <small>{currency}</small></span>}
          <div className="shift-line">
            {!d ? <Skeleton width={160}/> : current
              ? <><StatusPill tone="success" icon={LockOpen}>Смена №{current.id} открыта</StatusPill><span className="muted">с {dateTimeText(current.openedAt) !== '—' ? dateTimeText(current.openedAt) : dateText(current.date)}, начальный остаток {money(current.openingBalance)} {currency}</span></>
              : <StatusPill tone="warning" icon={Lock}>Смена закрыта — проведение недоступно</StatusPill>}
          </div>
          {d && canManage && (current
            ? <div><Button variant="secondary" icon={Lock} onClick={() => setClosing(true)}>Закрыть смену с пересчётом</Button></div>
            : <form className="shift-line" onSubmit={openShift}><label className="field" style={{margin: 0}}><span>Начальный остаток, {currency}</span><input inputMode="decimal" value={opening} placeholder={money(d.balance.balance)} onChange={e => setOpening(e.target.value)} aria-describedby="opening-hint"/></label><Button type="submit" icon={LockOpen} loading={busy}>Открыть смену</Button><span id="opening-hint" className="field-hint">По умолчанию — расчётный остаток по проведённым документам.</span></form>)}
        </section>
        {canCreate && <div className="cashier-actions">
          <Button size="lg" variant="secondary" className="ui-btn-income" icon={ArrowDownLeft} onClick={() => onOperation('income', boxId)} disabled={!d}>Приход<small className="muted">ПКО</small></Button>
          <Button size="lg" variant="secondary" className="ui-btn-expense" icon={ArrowUpRight} onClick={() => onOperation('expense', boxId)} disabled={!d}>Расход<small className="muted">РКО</small></Button>
          {d && !current && <p className="field-hint" style={{gridColumn: '1 / -1'}}>Черновик можно создать и без смены, но провести — только в открытую смену.</p>}
        </div>}
      </div>
      <div className="ui-grid-2">
        <Card title="Последние операции" subtitle="10 последних документов кассы за 30 дней" actions={<a className="text-btn" href={'#/operations?box=' + boxId}><History size={16}/>Весь журнал</a>}>
          {!d ? <SkeletonBlock rows={4}/> : d.ops.length ? <ul className="ui-list">{d.ops.map(row => <li key={row.id}>
            <div className="grow"><button type="button" className="document-btn" onClick={() => onDetails(row)}>{row.documentNumber || 'Черновик №' + row.id}</button><small>{dateText(row.date)} · {row.description}</small></div>
            <div className="shift-line"><OperationStatusPill status={row.status}/><ApprovalPill status={row.approvalStatus}/></div>
            <Amount value={row.amount} type={row.type}/>
          </li>)}</ul> : <EmptyState title="Операций пока нет" text="Зарегистрируйте первый приход или расход." action={canCreate && <Button icon={ArrowDownLeft} onClick={() => onOperation('income', boxId)}>Новый приход</Button>}/>}
        </Card>
        <Card title="Квитанции к приёму" subtitle="Деньги, переданные торговыми представителями">
          {!d ? <SkeletonBlock rows={2}/> : !can(user, 'receipt.accept') ? <p className="muted">Приём квитанций выполняет кассир или бухгалтер.</p> : d.receipts.length ? <ul className="ui-list">{d.receipts.map(r => <li key={r.id}>
            <Receipt size={18} aria-hidden="true"/>
            <div className="grow"><strong>№{r.id} · {r.counterparty}</strong><small>{r.representative || 'Представитель'} · {r.basisReference || ''}</small></div>
            <Amount value={r.amount}/>
            <Button size="sm" variant="secondary" disabled={busy || !current} title={!current ? 'Откройте смену' : undefined} onClick={() => accept(r)}>Принять</Button>
          </li>)}</ul> : <EmptyState icon={Receipt} title="Нет квитанций" text="Переданные в кассу квитанции появятся здесь."/>}
        </Card>
      </div>
    </>}
    {closing && current && <CloseShiftDrawer shift={current} expected={d?.balance.balance} currency={currency} onClose={() => setClosing(false)} onDone={message => { setClosing(false); onNotice?.(message); reload(); }}/>}
  </div>;
}
