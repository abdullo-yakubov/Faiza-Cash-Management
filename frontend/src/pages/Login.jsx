import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth.jsx'
import { Alert, Field, useSubmit } from '../components/ui.jsx'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [form, setForm] = useState({ login: '', password: '' })
  const { busy, error, submit } = useSubmit(async () => {
    await login(form.login, form.password)
    navigate('/', { replace: true }) // после входа всегда начинаем с главной
  })
  return (
    <div className="login-page">
      <form className="login-card" onSubmit={submit}>
        <div className="brand login-brand">
          <span className="brand-mark">F</span>
          <div>
            <div className="brand-name">FAIZA GOLD</div>
            <div className="brand-sub">Система кассового учёта ООО «Фаиза»</div>
          </div>
        </div>
        <Alert error={error} />
        <Field label="Логин">
          <input autoFocus autoComplete="username" value={form.login} required
            onChange={(e) => setForm({ ...form, login: e.target.value })} />
        </Field>
        <Field label="Пароль">
          <input type="password" autoComplete="current-password" value={form.password} required
            onChange={(e) => setForm({ ...form, password: e.target.value })} />
        </Field>
        <button className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Вход…' : 'Войти'}</button>
      </form>
    </div>
  )
}
