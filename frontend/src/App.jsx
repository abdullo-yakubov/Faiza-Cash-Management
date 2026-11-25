import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth.jsx'
import Layout from './components/Layout.jsx'
import Login from './pages/Login.jsx'
import ChangePassword from './pages/ChangePassword.jsx'
import Dashboard from './pages/Dashboard.jsx'
import Cash from './pages/Cash.jsx'
import Transfers from './pages/Transfers.jsx'
import Reports from './pages/Reports.jsx'
import Directories from './pages/Directories.jsx'
import Users from './pages/Users.jsx'
import Audit from './pages/Audit.jsx'
import { can } from './lib/permissions.js'

function Routed() {
  const { user } = useAuth()
  if (user === undefined) return <div className="splash">Загрузка…</div>
  if (user === null) return <Login />
  if (user.mustChangePassword) return <ChangePassword forced />

  return (
    <Layout>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        {can.operate(user) && <Route path="/cash" element={<Cash />} />}
        <Route path="/transfers" element={<Transfers />} />
        <Route path="/reports" element={<Reports />} />
        <Route path="/directories" element={<Directories />} />
        {can.manageUsers(user) && <Route path="/users" element={<Users />} />}
        {can.viewAudit(user) && <Route path="/audit" element={<Audit />} />}
        <Route path="/password" element={<ChangePassword />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Layout>
  )
}

export default function App() {
  return (
    <AuthProvider>
      <Routed />
    </AuthProvider>
  )
}
