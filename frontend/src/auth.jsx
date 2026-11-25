import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, setUnauthorizedHandler } from './lib/api.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [user, setUser] = useState(undefined) // undefined — ещё загружается, null — не вошёл

  useEffect(() => {
    setUnauthorizedHandler(() => setUser(null))
    api('/auth/me').then(setUser).catch(() => setUser(null))
  }, [])

  const login = useCallback(async (login, password) => {
    const me = await api('/auth/login', { method: 'POST', body: { login, password } })
    setUser(me)
  }, [])

  const logout = useCallback(async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => {})
    setUser(null)
  }, [])

  const value = useMemo(() => ({ user, setUser, login, logout }), [user, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  return useContext(AuthContext)
}
