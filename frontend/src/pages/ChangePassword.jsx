import { useState } from 'react'
import { useAuth } from '../auth.jsx'
import { api } from '../lib/api.js'
import { Alert, Field, useSubmit } from '../components/ui.jsx'

export default function ChangePassword({ forced = false }) {
  const { setUser, logout } = useAuth()
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', repeat: '' })
  const [done, setDone] = useState(false)
  const { busy, error, submit, setError } = useSubmit(async () => {
    if (form.newPassword !== form.repeat) {
      setError(new Error('Новые пароли не совпадают.'))
      return
    }
    const me = await api('/auth/change-password', {
      method: 'POST', body: { currentPassword: form.currentPassword, newPassword: form.newPassword },
    })
    setUser(me)
    setDone(true)
    setForm({ currentPassword: '', newPassword: '', repeat: '' })
  })
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value })

  const body = (
    <form className={forced ? 'login-card' : 'card narrow'} onSubmit={submit}>
      <h2>{forced ? 'Задайте свой пароль' : 'Смена пароля'}</h2>
      {forced && <p className="muted">Это первый вход или пароль сбросил администратор. Придумайте новый пароль.</p>}
      <Alert error={error} />
      {done && <Alert kind="ok">Пароль изменён.</Alert>}
      <Field label="Текущий пароль">
        <input type="password" autoComplete="current-password" value={form.currentPassword} onChange={set('currentPassword')} required />
      </Field>
      <Field label="Новый пароль" hint="Не короче 8 символов, буквы и цифры.">
        <input type="password" autoComplete="new-password" value={form.newPassword} onChange={set('newPassword')} required />
      </Field>
      <Field label="Повторите новый пароль">
        <input type="password" autoComplete="new-password" value={form.repeat} onChange={set('repeat')} required />
      </Field>
      <div className="row-actions">
        <button className="btn btn-primary" disabled={busy}>Сохранить</button>
        {forced && <button type="button" className="btn" onClick={logout}>Выйти</button>}
      </div>
    </form>
  )
  return forced ? <div className="login-page">{body}</div> : body
}
