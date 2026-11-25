// Права ролей в интерфейсе. Сервер проверяет те же правила, здесь они нужны только чтобы скрыть лишние кнопки.
export const can = {
  operate: (user) => ['Admin', 'Accountant', 'Cashier'].includes(user?.role),
  manageDirectories: (user) => ['Admin', 'Accountant'].includes(user?.role),
  manageStructure: (user) => user?.role === 'Admin',
  manageUsers: (user) => user?.role === 'Admin',
  viewAudit: (user) => ['Admin', 'Accountant', 'Manager'].includes(user?.role),
}

export function menuFor(user) {
  const items = [{ to: '/', label: 'Главная', icon: '◧' }]
  if (can.operate(user)) items.push({ to: '/cash', label: 'Касса', icon: '₸' })
  items.push({ to: '/transfers', label: 'Переводы', icon: '⇄' })
  items.push({ to: '/reports', label: 'Отчёты', icon: '▤' })
  items.push({ to: '/directories', label: 'Справочники', icon: '☰' })
  if (can.manageUsers(user)) items.push({ to: '/users', label: 'Пользователи', icon: '☺' })
  if (can.viewAudit(user)) items.push({ to: '/audit', label: 'Журнал', icon: '⏱' })
  return items
}
