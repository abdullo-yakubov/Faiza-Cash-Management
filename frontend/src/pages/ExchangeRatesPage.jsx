import React, {useState} from 'react';
import {Coins, Save} from 'lucide-react';
import {exchangeRates} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, DataTable, EmptyState, ErrorState, Field} from '../components/ui';
import {today, dateText} from '../utils/format.js';
import {can} from '../utils/permissions.js';

const currencies = [['USD', 'Доллар США'], ['EUR', 'Евро'], ['RUB', 'Российский рубль'], ['CNY', 'Китайский юань'], ['KZT', 'Казахстанский тенге'], ['UZS', 'Узбекский сум']];
// Курс — строка, до 6 знаков после разделителя; float не используется.
export function normalizeRate(value) {
  const text = String(value).trim().replace(/[\s ]/g, '').replace(',', '.');
  if (!/^\d{1,6}(\.\d{1,6})?$/.test(text) || /^0+(\.0+)?$/.test(text)) throw new Error('Курс — положительное число, не более 6 знаков после разделителя.');
  return text.replace(/^0+(?=\d)/, '');
}

/** Курсы валют к TJS (контракт п.6). Касса работает в одной валюте; курсы нужны для сводных сумм. */
export function ExchangeRatesPage({user, onNotice}) {
  const [date, setDate] = useState(today());
  const list = useAsync(signal => exchangeRates.list(date, signal), [date]);
  const [form, setForm] = useState({currency: 'USD', rateToBase: ''}), [busy, setBusy] = useState(false), [error, setError] = useState('');
  async function submit(e) {
    e.preventDefault(); setError('');
    let rate; try { rate = normalizeRate(form.rateToBase); } catch (err) { setError(err.message); return; }
    setBusy(true);
    try { await exchangeRates.save({currency: form.currency, date, rateToBase: rate}); onNotice?.(`Курс ${form.currency} на ${dateText(date)} сохранён.`); setForm({...form, rateToBase: ''}); list.reload(); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  const rows = Array.isArray(list.data) ? list.data : list.data?.items || [];
  return <div className="ui-grid-2">
    <Card title={'Курсы на ' + dateText(date)} subtitle="Сколько сомони (TJS) за 1 единицу валюты">
      <div className="ui-toolbar"><Field label="Дата"><input type="date" value={date} max={today()} onChange={e => setDate(e.target.value || today())}/></Field></div>
      {list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload}/> :
        <DataTable loading={list.status === 'loading' && !list.data} rowKey={r => r.currency + r.date} rows={rows} caption="Курсы валют"
          columns={[{key: 'currency', title: 'Валюта', sortable: true, render: r => <><strong>{r.currency}</strong><small>{currencies.find(c => c[0] === r.currency)?.[1] || ''}</small></>}, {key: 'rateToBase', title: 'Курс к TJS', align: 'right', render: r => <span className="ui-amount">{String(r.rateToBase).replace('.', ',')}</span>}]}
          empty={<EmptyState icon={Coins} title="Курсов на дату нет" text="Сводные суммы в других валютах не будут пересчитаны в TJS."/>}/>}
    </Card>
    <Card title="Установить курс">
      {can(user, 'rates.manage') ? <form onSubmit={submit} className="ui-stack" noValidate>
        <Field label="Валюта"><select value={form.currency} onChange={e => setForm({...form, currency: e.target.value})}>{currencies.map(([code, name]) => <option key={code} value={code}>{code} — {name}</option>)}</select></Field>
        <Field label={`Курс на ${dateText(date)}, TJS за 1 ${form.currency} *`}><input inputMode="decimal" value={form.rateToBase} placeholder="Например, 10,9450" onChange={e => setForm({...form, rateToBase: e.target.value})}/></Field>
        {error && <div className="error" role="alert">{error}</div>}
        <div><Button type="submit" icon={Save} loading={busy}>Сохранить курс</Button></div>
        <p className="field-hint">Переводы между кассами в разных валютах запрещены. Курс используется только для пересчёта сводных показателей.</p>
      </form> : <p className="muted">Курсы изменяют администратор и бухгалтер.</p>}
    </Card>
  </div>;
}
