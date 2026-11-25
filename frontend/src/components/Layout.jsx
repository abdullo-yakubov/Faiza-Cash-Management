import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth.jsx'
import { menuFor } from '../lib/permissions.js'
import { roleNames } from '../lib/format.js'

export default function Layout({ children }) {
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)
  return (
    <div className={`shell ${open ? 'nav-open' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">F</span>
          <div>
            <div className="brand-name">FAIZA GOLD</div>
            <div className="brand-sub">кассовый учёт</div>
          </div>
        </div>
        <nav>
          {menuFor(user).map((item) => (
            <NavLink key={item.to} to={item.to} end={item.to === '/'} onClick={() => setOpen(false)}
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
              <span className="nav-icon" aria-hidden>{item.icon}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="userbox">
          <div className="user-name">{user.fullName}</div>
          <div className="user-role">{roleNames[user.role]} · {user.login}</div>
          <div className="user-actions">
            <NavLink to="/password" onClick={() => setOpen(false)}>Сменить пароль</NavLink>
            <button className="link" onClick={logout}>Выйти</button>
          </div>
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <button className="btn-icon menu-btn" onClick={() => setOpen(!open)} aria-label="Меню">☰</button>
          <span className="topbar-title">ООО «Фаиза»</span>
        </header>
        <main className="content">{children}</main>
      </div>
    </div>
  )
}
