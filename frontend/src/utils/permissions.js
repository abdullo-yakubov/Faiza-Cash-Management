// Единая точка проверки прав в интерфейсе (волна 1, разделение обязанностей).
// Сервер — главный: здесь только скрываем действия, которые всё равно вернут 403.
// admin больше НЕ выполняет финансовых операций, только настройку и просмотр.

export const ROLES = ['admin', 'accountant', 'cashier', 'manager', 'auditor'];

export const roleDescriptions = {
  admin: 'Настройка системы, пользователи, кассы, доступы, резервные копии и курсы валют. Финансовые документы — только просмотр.',
  accountant: 'Полный кассовый учёт: документы любых авторов, сторно, смены, переводы, авансы, инвентаризация, справочники.',
  cashier: 'Свои кассовые документы, открытие и закрытие смены, переводы, приём квитанций, отправка расходов на согласование.',
  manager: 'Руководитель: согласует расходы, ведёт задолженности и квитанции, проводит инвентаризацию, смотрит отчёты и аудит.',
  auditor: 'Только чтение: операции, отчёты, задолженности и журнал аудита без права изменений.'
};

const ALL = ROLES;
// action → роли, которым действие разрешено без дополнительных условий
const matrix = {
  // просмотр
  'cash.view': ALL,
  'debts.view': ['admin', 'accountant', 'manager', 'auditor'],
  'audit.view': ['admin', 'accountant', 'manager', 'auditor'],
  'attachments.view': ALL,
  'advances.view': ALL,
  'inventory.view': ALL,
  'approvals.view': ['admin', 'accountant', 'manager', 'auditor'],
  'reports.view': ALL,
  // кассовые документы
  'operation.create': ['accountant', 'cashier'],
  'operation.edit': ['accountant', 'cashier'],      // cashier — только свой (см. ownOnly)
  'operation.post': ['accountant', 'cashier'],
  'operation.delete': ['accountant', 'cashier'],
  'operation.reverse': ['accountant'],
  'operation.submitApproval': ['accountant', 'cashier'],
  'operation.approve': ['manager'],                 // не свой (см. notOwn)
  // смены, переводы
  'shift.manage': ['accountant', 'cashier'],
  'transfer.create': ['accountant', 'cashier'],
  // задолженности и квитанции
  'debt.create': ['accountant', 'manager'],
  'debt.allocate': ['accountant', 'manager'],
  'debt.cancel': ['accountant'],
  'receipt.create': ['accountant', 'manager'],
  'receipt.accept': ['accountant', 'cashier'],
  // вложения
  'attachment.manage': ['accountant', 'cashier', 'manager'],
  // авансы, инвентаризация
  'advance.manage': ['accountant'],
  'inventory.create': ['accountant', 'manager'],
  // справочники и администрирование
  'directory.manage': ['admin', 'accountant'],
  'cashbox.manage': ['admin'],
  'settings.manage': ['admin'],
  'users.manage': ['admin'],
  'backups.manage': ['admin'],
  'admin.health': ['admin'],
  'logo.manage': ['admin'],
  'denominations.manage': ['admin'],
  'rates.manage': ['admin', 'accountant'],
  'avatar.manage': ALL
};

// Действия, где кассир ограничен своими документами.
const ownOnlyForCashier = new Set(['operation.edit', 'operation.post', 'operation.delete', 'operation.submitApproval', 'attachment.manage']);
// Действия, где автор не может выполнить действие над своим документом.
const notOwn = new Set(['operation.approve']);

export const hasRole = (user, role) => Boolean(user?.roles?.includes(role));

/**
 * can(user, action, resource?)
 * resource — документ: { authorId, status, approvalStatus, ownerType }.
 */
export function can(user, action, resource) {
  if (!user || !Array.isArray(user.roles)) return false;
  const allowed = matrix[action];
  if (!allowed) return false;
  const roles = user.roles.filter(role => allowed.includes(role));
  if (!roles.length) return false;
  if (resource) {
    const own = resource.authorId !== undefined && resource.authorId === user.id;
    if (notOwn.has(action) && own) return false;
    if (ownOnlyForCashier.has(action)) {
      // manager управляет вложениями только у квитанций
      if (action === 'attachment.manage') {
        const viaAccountant = roles.includes('accountant');
        const viaCashier = roles.includes('cashier') && own;
        const viaManager = roles.includes('manager') && resource.ownerType === 'receipt';
        if (!viaAccountant && !viaCashier && !viaManager) return false;
      } else if (!roles.includes('accountant') && !own) return false;
    }
    if (['operation.edit', 'operation.post', 'operation.delete', 'operation.submitApproval', 'operation.approve'].includes(action) && resource.status && resource.status !== 'draft') return false;
    if (action === 'operation.submitApproval' && ['pending', 'approved'].includes(resource.approvalStatus)) return false;
    if (action === 'operation.approve' && resource.approvalStatus !== 'pending') return false;
  }
  return true;
}

/** Есть ли у пользователя хоть одна финансовая (изменяющая) функция. */
export const canTouchMoney = user => ['operation.create', 'shift.manage', 'transfer.create', 'debt.create', 'receipt.accept', 'advance.manage', 'inventory.create'].some(a => can(user, a));

export const permissionMatrix = matrix;
