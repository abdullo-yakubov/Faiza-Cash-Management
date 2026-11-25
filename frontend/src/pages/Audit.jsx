import { useState } from 'react'
import { api } from '../lib/api.js'
import { dateTime } from '../lib/format.js'
import { Alert, Empty, PageHead, useLoad } from '../components/ui.jsx'

const actions = {
  login: 'Вход', login_failed: 'Неудачный вход', password_changed: 'Смена пароля', password_reset: 'Сброс пароля',
  user_created: 'Новый пользователь', user_updated: 'Изменён пользователь',
  branch_created: 'Новый филиал', branch_updated: 'Изменён филиал', register_created: 'Новая касса', register_updated: 'Изменена касса',
  category_created: 'Новая статья', category_updated: 'Изменена статья',
  counterparty_created: 'Новый контрагент', counterparty_updated: 'Изменён контрагент',
  shift_opened: 'Смена открыта', shift_closed: 'Смена закрыта',
  operation_created: 'Операция', operation_reversed: 'Сторно', transfer_created: 'Перевод',
}

export default function Audit() {
  const [action, setAction] = useState('')
  const log = useLoad(() => api(`/audit?take=300${action ? `&action=${action}` : ''}`), [action])
  return (
    <>
      <PageHead title="Журнал действий">
        <select value={action} onChange={(e) => setAction(e.target.value)} aria-label="Действие">
          <option value="">Все действия</option>
          {Object.entries(actions).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
      </PageHead>
      <Alert error={log.error} />
      {log.data?.length === 0 && <Empty>Записей нет.</Empty>}
      {log.data?.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Время</th><th>Пользователь</th><th>Действие</th><th>Объект</th><th>Подробности</th></tr></thead>
            <tbody>
              {log.data.map((a) => (
                <tr key={a.id} className={a.action === 'login_failed' ? 'warn-row' : ''}>
                  <td className="nowrap">{dateTime(a.at)}</td>
                  <td>{a.userLogin}</td>
                  <td>{actions[a.action] || a.action}</td>
                  <td className="nowrap">{a.entityId}</td>
                  <td className="small mono">{a.details}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
