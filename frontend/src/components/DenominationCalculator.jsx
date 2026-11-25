import React, {useId} from 'react';
import {Banknote, Coins, AlertTriangle, CheckCircle2, TrendingUp} from 'lucide-react';
import {denominationTotal, reconcile, absAmount} from '../utils/money.js';
import {money} from '../utils/format.js';

/** Пустые строки калькулятора из справочника номиналов. */
export const emptyCounts = list => list.map(d => ({value: d.value, kind: d.kind, count: ''}));
/** Только заполненные строки в формате контракта: [{ value, count }]. */
export const filledRows = rows => denominationTotal(rows).lines.filter(l => l.count > 0).map(({value, count}) => ({value, count}));

const kindText = {shortage: 'Недостача', surplus: 'Излишек', match: 'Совпадает с расчётом'};
const kindIcon = {shortage: AlertTriangle, surplus: TrendingUp, match: CheckCircle2};

/**
 * Калькулятор пересчёта по купюрам: ввод количества, автоитог, разница с расчётным остатком.
 * Вся арифметика — в целых дирамах (utils/money.js).
 */
export function DenominationCalculator({rows, onChange, expected, currency = 'TJS', disabled}) {
  const id = useId();
  const result = denominationTotal(rows);
  const check = expected != null ? reconcile(result.total, expected) : null;
  const set = (index, count) => onChange(rows.map((row, i) => i === index ? {...row, count} : row));
  const groups = [['banknote', 'Банкноты', Banknote], ['coin', 'Монеты', Coins]];
  return <div className="denoms">
    {groups.map(([kind, title, Icon]) => {
      const items = rows.map((row, index) => ({row, index})).filter(x => (x.row.kind || 'banknote') === kind);
      if (!items.length) return null;
      return <React.Fragment key={kind}>
        <div className="denom-group" id={id + kind}>{title}</div>
        {items.map(({row, index}) => {
          const invalid = result.invalid.includes(index);
          const inputId = `${id}-${index}`;
          return <div className="denom-row" key={kind + row.value} role="group" aria-labelledby={inputId + '-label'}>
            <label htmlFor={inputId} id={inputId + '-label'}><Icon size={15} aria-hidden="true"/>{money(row.value)}</label>
            <input id={inputId} inputMode="numeric" pattern="[0-9]*" autoComplete="off" placeholder="0" maxLength={7} value={row.count} disabled={disabled}
              aria-invalid={invalid} aria-label={`Количество: ${money(row.value)} ${currency}`} onChange={e => set(index, e.target.value.replace(/[^\d]/g, ''))}/>
            <output htmlFor={inputId}>{result.lines[index].count ? money(result.lines[index].amount) : '—'}</output>
          </div>;
        })}
      </React.Fragment>;
    })}
    {result.invalid.length > 0 && <p className="field-error" role="alert">Количество — целое неотрицательное число.</p>}
    <div className={'denom-summary ' + (check?.kind || '')} aria-live="polite">
      <div className="total"><span>Итого по пересчёту</span><span>{money(result.total)} {currency}</span></div>
      {check && <>
        <div><span>Расчётный остаток</span><span>{money(expected)} {currency}</span></div>
        <div><strong className="shift-line">{React.createElement(kindIcon[check.kind], {size: 17, 'aria-hidden': true})}{kindText[check.kind]}</strong>
          <strong>{check.kind === 'match' ? '0,00' : (check.kind === 'shortage' ? '− ' : '+ ') + money(absAmount(check.difference))} {currency}</strong></div>
      </>}
    </div>
  </div>;
}
