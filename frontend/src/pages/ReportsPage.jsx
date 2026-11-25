import React from 'react';
import {Empty,Pager} from '../components/ui';
import {FinanceStats,FinanceCharts} from '../components/FinancePanels';
import {dateText,money} from '../utils/format.js';
/** ТЗ §5.7: every report is exported with the filters shown on the page (cashbox, period). */
export function ReportExports({box,boxes=[],from,to,canViewDebts,busy,onDownload}) {
  const current=boxes.find(b=>String(b.id)===String(box));
  const scoped=new URLSearchParams({cashboxId:box,from,to}).toString(),period=new URLSearchParams({from,to}).toString();
  const reports=[
    box&&['shifts','Отчёт по кассовым сменам',`Касса «${current?.name||box}»: начальный, расчётный и фактический остаток, расхождения`,`/reports/shifts.{f}?${scoped}`],
    current?.branchId&&['branch',`Отчёт по филиалу «${current.branch||current.branchId}»`,'Все доступные кассы филиала с нарастающим остатком',`/reports/branch.{f}?branchId=${current.branchId}&${period}`],
    box&&['transfers','Межкассовые переводы','Парные РКО/ПКО по выбранной кассе за период',`/reports/transfers.{f}?${scoped}`],
    canViewDebts&&['settlements','Задолженности и платежи','Все доступные задолженности на сегодня, с представителем и остатком',`/reports/settlements.{f}`],
    canViewDebts&&['representatives','Торговые представители','Суммы у представителя, переданные кассиру и принятые кассой',`/reports/representatives.{f}?${period}`]
  ].filter(Boolean);
  return <section className="panel report-exports" aria-labelledby="report-exports-title">
    <div className="panel-heading"><div><h2 id="report-exports-title">Выгрузка отчётов</h2><p>Период {from} — {to}. Файл содержит применённые фильтры и дату формирования.</p></div></div>
    {reports.map(([id,title,hint,path])=><div className="export-row" key={id}><div><strong>{title}</strong><small className="muted"> · {hint}</small></div><div className="row-actions">
      <button className="button secondary" disabled={busy} aria-label={title+': CSV'} onClick={()=>onDownload(path.replace('{f}','csv'),`Faiza-${id}.csv`)}>CSV</button>
      <button className="button secondary" disabled={busy} aria-label={title+': PDF'} onClick={()=>onDownload(path.replace('{f}','pdf'),`Faiza-${id}.pdf`)}>PDF</button>
    </div></div>)}
  </section>;
}
export function ReportsPage({book,points,distribution,currency,onPage,onExport,onDetails,busy,exportsPanel}) {
  return <><FinanceStats summary={book} currency={currency}/>{exportsPanel}<FinanceCharts points={points} distribution={distribution} currency={currency}/><section className="panel"><div className="panel-heading"><div><h2>Учебная кассовая книга</h2><p>По дате, порядку проведения и номеру записи. Не является официальной бухгалтерской формой.</p></div><div className="row-actions"><button className="button secondary" disabled={busy} onClick={()=>onExport('csv')}>Скачать CSV</button><button className="button secondary" disabled={busy} onClick={()=>onExport('pdf')}>Скачать PDF</button></div></div><p className="export-explanation">Экспортируется весь выбранный период, а не текущая страница. Начальный остаток: <strong>{money(book.opening)} {currency}</strong>.</p><div className="table-scroll"><table><thead><tr><th>Дата</th><th>Документ</th><th>Основание / контрагент</th><th className="numeric">Приход</th><th className="numeric">Расход</th><th className="numeric">Остаток, {currency}</th></tr></thead><tbody>{book.items.map(row=><tr key={row.id}><td>{dateText(row.date)}</td><td><button className="document-btn" onClick={()=>onDetails(row)}>{row.documentNumber}</button></td><td><strong>{row.description}</strong><small>{row.counterparty||'Контрагент не указан'}</small></td><td className="numeric">{row.type==='income'?money(row.amount):'—'}</td><td className="numeric">{row.type==='expense'?money(row.amount):'—'}</td><td className="numeric amount">{money(row.balance)}</td></tr>)}</tbody></table>{!book.items.length&&<Empty text="Нет проведённых документов за выбранный период."/>}</div><Pager index={book.page} total={book.total} onChange={onPage}/></section></>;
}
