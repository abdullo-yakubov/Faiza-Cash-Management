import { useState } from 'react'
import { api } from '../lib/api.js'
import { roleNames } from '../lib/format.js'
import { Alert, Field, Modal, PageHead, useLoad, useSubmit } from '../components/ui.jsx'

export default function Users() {
  const users = useLoad(() => api('/users'), [])
  const registers = useLoad(() => api('/registers'), [])
  const [editing, setEditing] = useState(null)
  const [resetting, setResetting] = useState(null)
  const regName = (id) => {
    const r = registers.data?.find((x) => x.id === id)
    return r ? `${r.name} (${r.currency})` : `#${id}`
  }
  return (
    <>
      <PageHead title="Пользователи">
        <button className="btn btn-primary" onClick={() => setEditing({})}>Добавить пользователя</button>
      </PageHead>
      <Alert error={users.error} />
      {users.data && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Логин</th><th>ФИО</th><th>Роль</th><th>Кассы</th><th>Статус</th><th /></tr></thead>
            <tbody>
              {users.data.map((u) => (
                <tr key={u.id} className={u.isActive ? '' : 'inactive'}>
                  <td className="mono">{u.login}</td>
                  <td>{u.fullName}</td>
                  <td>{roleNames[u.role]}</td>
                  <td className="small">{u.role === 'Cashier' ? (u.registerIds.map(regName).join(', ') || '—') : 'все'}</td>
                  <td>
                    {u.isActive ? <span className="badge badge-ok">активен</span> : <span className="badge">заблокирован</span>}
                    {u.mustChangePassword && <span className="badge badge-warn">ждёт смены пароля</span>}
                  </td>
                  <td className="nowrap">
                    <button className="btn btn-small" onClick={() => setEditing(u)}>Изменить</button>
                    <button className="btn btn-small" onClick={() => setResetting(u)}>Сбросить пароль</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing && <UserForm row={editing} registers={registers.data || []} onClose={() => setEditing(null)} onSaved={users.reload} />}
      {resetting && <ResetPassword user={resetting} onClose={() => setResetting(null)} onSaved={users.reload} />}
    </>
  )
}

function UserForm({ row, registers, onClose, onSaved }) {
  const isNew = !row.id
  const [f, setF] = useState({
    login: row.login || '', fullName: row.fullName || '', role: row.role || 'Cashier', password: '',
    isActive: row.isActive ?? true, registerIds: row.registerIds || [],
  })
  const toggle = (id) => setF({ ...f, registerIds: f.registerIds.includes(id) ? f.registerIds.filter((x) => x !== id) : [...f.registerIds, id] })
  const { busy, error, submit } = useSubmit(async () => {
    if (isNew) await api('/users', { method: 'POST', body: f })
    else await api(`/users/${row.id}`, { method: 'PUT', body: f })
    await onSaved()
    onClose()
  })
  return (
    <Modal title={isNew ? 'Новый пользователь' : `Пользователь ${row.login}`} onClose={onClose}>
      <form onSubmit={submit}>
        <Alert error={error} />
        {isNew && (
          <Field label="Логин" hint="Латинские буквы, цифры, точка, дефис.">
            <input value={f.login} onChange={(e) => setF({ ...f, login: e.target.value })} maxLength={50} required />
          </Field>
        )}
        <Field label="ФИО"><input value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} maxLength={150} required /></Field>
        <Field label="Роль">
          <select value={f.role} onChange={(e) => setF({ ...f, role: e.target.value })}>
            {Object.entries(roleNames).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        {f.role === 'Cashier' && (
          <fieldset className="checks">
            <legend>Доступ к кассам</legend>
            {registers.map((r) => (
              <label className="check" key={r.id}>
                <input type="checkbox" checked={f.registerIds.includes(r.id)} onChange={() => toggle(r.id)} /> {r.branchName} / {r.name} ({r.currency})
              </label>
            ))}
          </fieldset>
        )}
        {isNew && (
          <Field label="Временный пароль" hint="Пользователь сменит его при первом входе. Не короче 8 символов, буквы и цифры.">
            <input type="text" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} required autoComplete="off" />
          </Field>
        )}
        {!isNew && <label className="check"><input type="checkbox" checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} /> Активен (может входить)</label>}
        <div className="row-actions">
          <button className="btn btn-primary" disabled={busy}>Сохранить</button>
          <button type="button" className="btn" onClick={onClose}>Отмена</button>
        </div>
      </form>
    </Modal>
  )
}

function ResetPassword({ user, onClose, onSaved }) {
  const [password, setPassword] = useState('')
  const { busy, error, submit } = useSubmit(async () => {
    await api(`/users/${user.id}/reset-password`, { method: 'POST', body: { password } })
    await onSaved()
    onClose()
  })
  return (
    <Modal title={`Сброс пароля: ${user.login}`} onClose={onClose}>
      <form onSubmit={submit}>
        <Alert error={error} />
        <Field label="Новый временный пароль" hint="Пользователь сменит его при следующем входе.">
          <input value={password} onChange={(e) => setPassword(e.target.value)} required autoComplete="off" />
        </Field>
        <div className="row-actions">
          <button className="btn btn-primary" disabled={busy}>Сбросить</button>
          <button type="button" className="btn" onClick={onClose}>Отмена</button>
        </div>
      </form>
    </Modal>
  )
}
