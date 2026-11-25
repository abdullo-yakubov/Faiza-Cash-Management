import { Link } from 'react-router-dom'
import { api } from '../lib/api.js'
import { money } from '../lib/format.js'
import { Alert, Empty, PageHead, useLoad } from '../components/ui.jsx'
import { useAuth } from '../auth.jsx'
import { can } from '../lib/permissions.js'

export default function Dashboard() {
  const { user } = useAuth()
  const { data, error, reload } = useLoad(() => api('/dashboard'), [])
  return (
    <>
      <PageHead title="Главная">
        <button className="btn" onClick={reload}>Обновить</button>
      </PageHead>
      <Alert error={error} />
      {data && (
        <>
          <div className="stats">
            {data.totals.map((t) => (
              <div className="stat" key={t.currency}>
                <div className="stat-label">Всего в кассах, {t.currency}</div>
                <div className="stat-value">{money(t.balance)}</div>
              </div>
            ))}
          </div>
          <h2 className="section-title">Кассы на {new Date(data.date).toLocaleDateString('ru-RU')}</h2>
          {data.registers.length === 0 ? (
            <Empty>Нет доступных касс. {can.manageStructure(user) ? 'Добавьте кассу в справочниках.' : 'Обратитесь к администратору.'}</Empty>
          ) : (
            <div className="cards">
              {data.registers.map((r) => (
                <div className="card register-card" key={r.id}>
                  <div className="register-head">
                    <div>
                      <div className="register-name">{r.name}</div>
                      <div className="muted">{r.branch}</div>
                    </div>
                    <span className={`badge ${r.hasOpenShift ? 'badge-ok' : ''}`}>{r.hasOpenShift ? 'Смена открыта' : 'Смена закрыта'}</span>
                  </div>
                  <div className="register-balance">{money(r.balance, r.currency)}</div>
                  <div className="register-today">
                    <span>Сегодня: <b className="plus">+{money(r.todayIncome)}</b></span>
                    <span><b className="minus">−{money(r.todayExpense)}</b></span>
                    <span className="muted">{r.todayCount} опер.</span>
                  </div>
                  {can.operate(user) && <Link className="btn btn-small" to={`/cash?register=${r.id}`}>Открыть кассу</Link>}
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </>
  )
}
