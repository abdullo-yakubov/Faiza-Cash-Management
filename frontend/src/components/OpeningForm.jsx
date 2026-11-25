import React,{useState} from 'react';
import {api} from '../services/api.js';
import {today} from '../utils/format.js';

/** One-time cashbox initial balance (admin only). The API rejects it after the first shift or posted document. */
export function OpeningForm({box,currency,onDone,onCancel}){
  const [form,setForm]=useState({date:today(),amount:''}),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const submit=async e=>{
    e.preventDefault();if(busy)return;
    if(!/^\d{1,16}(\.\d{1,2})?$/.test(form.amount.trim())){setError('Укажите неотрицательную сумму с точностью до дирама.');return;}
    setBusy(true);setError('');
    try{await api(`/cashboxes/${box.id}/opening`,{method:'POST',body:{date:form.date,amount:form.amount.trim()}});onDone();}
    catch(err){setError(err.message);}
    finally{setBusy(false);}
  };
  return <form onSubmit={submit} noValidate>
    <p className="field-hint">Начальный остаток задаётся один раз, до первой смены и первого проведённого документа. С этой даты касса участвует в остатках, графиках и отчётах.</p>
    <label className="field"><span>Дата начала учёта</span><input name="date" type="date" required max={today()} value={form.date} onChange={e=>setForm({...form,date:e.target.value})}/></label>
    <label className="field"><span>Сумма наличных, {currency}</span><input name="amount" inputMode="decimal" required value={form.amount} aria-invalid={Boolean(error)} aria-describedby={error?'opening-error':undefined} onChange={e=>setForm({...form,amount:e.target.value})}/></label>
    {error&&<p id="opening-error" className="field-error" role="alert">{error}</p>}
    <div className="modal-actions"><button type="button" className="button secondary" disabled={busy} onClick={onCancel}>Отмена</button><button className="button primary" disabled={busy}>{busy?'Сохранение…':'Задать начальный остаток'}</button></div>
  </form>;
}
