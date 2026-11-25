import { dateTime, money } from '../lib/format.js'
import { Modal } from './ui.jsx'

/** Печатная форма приходного или расходного кассового ордера. */
export default function OrderPrint({ op, register, onClose }) {
  const income = op.kind === 'Income'
  return (
    <Modal title="Кассовый ордер" onClose={onClose}>
      <div className="print-area order">
        <div className="order-company">ООО «Фаиза» · {register?.branchName}</div>
        <h3>{income ? 'Приходный' : 'Расходный'} кассовый ордер № {op.number}</h3>
        <table className="order-table">
          <tbody>
            <tr><th>Дата</th><td>{dateTime(op.createdAt)}</td></tr>
            <tr><th>Касса</th><td>{register?.name}</td></tr>
            <tr><th>{income ? 'Принято от' : 'Выдать'}</th><td>{op.counterparty || '—'}</td></tr>
            <tr><th>Статья</th><td>{op.category}</td></tr>
            <tr><th>Основание</th><td>{op.description || '—'}</td></tr>
            <tr><th>Сумма</th><td><b>{money(op.amount, register?.currency)}</b></td></tr>
          </tbody>
        </table>
        <div className="order-signs">
          <div>Кассир ____________ {op.createdBy}</div>
          <div>{income ? 'Сдал' : 'Получил'} ____________</div>
        </div>
      </div>
      <div className="row-actions no-print">
        <button className="btn btn-primary" onClick={() => window.print()}>Печать / PDF</button>
        <button className="btn" onClick={onClose}>Закрыть</button>
      </div>
    </Modal>
  )
}
