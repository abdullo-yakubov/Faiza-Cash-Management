import React, {useState} from 'react';
import {Check, X, ClipboardCheck, FileText} from 'lucide-react';
import {approvals as approvalApi, listItems, listTotal} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, DataTable, EmptyState, ErrorState, ApprovalPill, Amount, Modal} from '../components/ui';
import {Field} from '../components/ui';
import {dateText, dateTimeText, money} from '../utils/format.js';
import {can} from '../utils/permissions.js';
import {toCents} from '../utils/money.js';

const tabs = [['pending', 'Ожидают'], ['approved', 'Согласованные'], ['rejected', 'Отклонённые']];

/** Очередь согласования расходов (контракт п.1). */
export function ApprovalsPage({user, boxes = [], currency = 'TJS', onDetails, onChanged, onNotice}) {
  const [status, setStatus] = useState('pending'), [box, setBox] = useState(''), [index, setIndex] = useState(1);
  const [dialog, setDialog] = useState(null), [text, setText] = useState(''), [busy, setBusy] = useState(false), [error, setError] = useState('');
  const list = useAsync(signal => approvalApi.list({status, cashboxId: box, page: index, pageSize: 15}, signal), [status, box, index]);
  const rows = listItems(list.data);
  async function decide() {
    if (dialog.mode === 'reject' && !text.trim()) { setError('Укажите причину отклонения — она обязательна.'); return; }
    setBusy(true); setError('');
    try {
      if (dialog.mode === 'approve') await approvalApi.approve(dialog.row.id, text.trim() || undefined);
      else await approvalApi.reject(dialog.row.id, text.trim());
      onNotice?.(dialog.mode === 'approve' ? `Расход №${dialog.row.id} согласован. Автор может провести документ.` : `Расход №${dialog.row.id} отклонён. Причина передана автору.`);
      setDialog(null); setText(''); list.reload(); onChanged?.();
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  const columns = [
    {key: 'date', title: 'Документ', sortable: true, render: row => <><button type="button" className="document-btn" onClick={e => { e.stopPropagation(); onDetails(row); }}>Черновик №{row.id}</button><small>{dateText(row.date)} · {row.cashbox || boxes.find(b => b.id === row.cashboxId)?.name || 'Касса №' + row.cashboxId}</small></>},
    {key: 'description', title: 'Основание', render: row => <><strong>{row.description}</strong><small>{row.category || 'Категория не указана'}{row.counterparty ? ' · ' + row.counterparty : ''}</small></>},
    {key: 'createdBy', title: 'Автор', sortable: true, render: row => row.createdBy || '—'},
    {key: 'approvalStatus', title: 'Статус', render: row => <><ApprovalPill status={row.approvalStatus}/>{row.approvalComment && <small title={row.approvalComment}>{row.approvalComment}</small>}{row.approvedAt && <small>{row.approvedBy} · {dateTimeText(row.approvedAt)}</small>}</>},
    {key: 'amount', title: 'Сумма, ' + currency, align: 'right', sortable: true, sortValue: row => toCents(row.amount), render: row => <Amount value={row.amount} type="expense"/>},
    {key: 'actions', title: <span className="sr-only">Действия</span>, render: row => {
      if (row.approvalStatus !== 'pending') return null;
      if (!can(user, 'operation.approve', row)) return <small className="muted">{row.authorId === user.id ? 'Свой расход согласует другой руководитель' : 'Только просмотр'}</small>;
      return <div className="row-actions"><Button size="sm" icon={Check} onClick={e => { e.stopPropagation(); setDialog({mode: 'approve', row}); setText(''); setError(''); }}>Согласовать</Button><Button size="sm" variant="danger" icon={X} onClick={e => { e.stopPropagation(); setDialog({mode: 'reject', row}); setText(''); setError(''); }}>Отклонить</Button></div>;
    }}
  ];
  return <div className="ui-stack">
    <div className="ui-toolbar" role="toolbar" aria-label="Фильтры согласования">
      <div className="quick-periods" role="tablist" aria-label="Статус">{tabs.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={status === id} className={status === id ? 'active' : ''} onClick={() => { setStatus(id); setIndex(1); }}>{label}</button>)}</div>
      <label className="field"><span>Касса</span><select value={box} onChange={e => { setBox(e.target.value); setIndex(1); }}><option value="">Все доступные</option>{boxes.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
    </div>
    <Card title={tabs.find(t => t[0] === status)[1] + ' согласования'} subtitle="Расходы не меньше порога проводятся только после согласования руководителя. Автор не может согласовать свой документ.">
      {list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload}/> :
        <DataTable loading={list.status === 'loading' && !list.data} columns={columns} rows={rows} page={index} total={listTotal(list.data)} onPage={setIndex} onRowClick={onDetails} caption="Расходы на согласовании"
          empty={<EmptyState icon={ClipboardCheck} title={status === 'pending' ? 'Очередь пуста' : 'Записей нет'} text={status === 'pending' ? 'Все расходы согласованы. Новые появятся здесь, когда кассир или бухгалтер отправит черновик.' : 'За выбранный фильтр документов нет.'}/>}/>}
    </Card>
    {dialog && <Modal title={dialog.mode === 'approve' ? 'Согласование расхода' : 'Отклонение расхода'} busy={busy} onClose={() => !busy && setDialog(null)}
      footer={<><Button variant="secondary" disabled={busy} onClick={() => setDialog(null)}>Отмена</Button><Button variant={dialog.mode === 'approve' ? 'primary' : 'danger'} icon={dialog.mode === 'approve' ? Check : X} loading={busy} onClick={decide}>{dialog.mode === 'approve' ? 'Согласовать' : 'Отклонить'}</Button></>}>
      {error && <div className="error" role="alert">{error}</div>}
      <p><strong>Черновик №{dialog.row.id}</strong> · {dialog.row.description}</p>
      <p>Сумма: <Amount value={dialog.row.amount} type="expense" currency={currency}/> · автор: {dialog.row.createdBy || '—'}</p>
      <Field label={dialog.mode === 'approve' ? 'Комментарий (необязательно)' : 'Причина отклонения *'}><textarea value={text} onChange={e => setText(e.target.value)} maxLength={500} required={dialog.mode === 'reject'} aria-required={dialog.mode === 'reject'} placeholder={dialog.mode === 'reject' ? 'Например: нет счёта поставщика' : ''}/></Field>
      <button type="button" className="text-btn" onClick={() => { const row = dialog.row; setDialog(null); onDetails(row); }}><FileText size={16}/>Открыть карточку и вложения</button>
      <p className="field-hint">Решение записывается в журнал аудита. {money(dialog.row.amount)} {currency}.</p>
    </Modal>}
  </div>;
}
