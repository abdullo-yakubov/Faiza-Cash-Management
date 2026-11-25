// Функции для маршрутов API-контракта волны 1. Деньги — строки.
import {api, upload, download} from './api.js';

const qs = params => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params || {})) if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
  const text = search.toString();
  return text ? '?' + text : '';
};
/** Список из ответа: массив или {items,total}. */
export const listItems = data => Array.isArray(data) ? data : Array.isArray(data?.items) ? data.items : [];
export const listTotal = data => Array.isArray(data) ? data.length : Number.isInteger(data?.total) ? data.total : listItems(data).length;

// 1. Согласование расходов
export const approvals = {
  list: ({status = 'pending', cashboxId, page, pageSize} = {}, signal) => api('/approvals' + qs({status, cashboxId, page, pageSize}), {signal}),
  submit: id => api(`/operations/${id}/submit-approval`, {method: 'POST'}),
  approve: (id, comment) => api(`/operations/${id}/approve`, {method: 'POST', body: comment ? {comment} : {}}),
  reject: (id, reason) => api(`/operations/${id}/reject`, {method: 'POST', body: {reason}})
};

// 2. Вложения
export const attachments = {
  list: (ownerType, ownerId, signal) => api('/attachments' + qs({ownerType, ownerId}), {signal}),
  add: (ownerType, ownerId, file) => { const form = new FormData(); form.set('ownerType', ownerType); form.set('ownerId', String(ownerId)); form.set('file', file); return upload('/attachments', form); },
  content: (id, signal) => api(`/attachments/${id}/content`, {raw: true, signal}),
  download: item => download(`/attachments/${item.id}/content`, item.fileName),
  remove: id => api(`/attachments/${id}`, {method: 'DELETE'})
};

// 3. Авансы
export const advances = {
  list: ({status, counterpartyId, overdue, page, pageSize} = {}, signal) => api('/advances' + qs({status, counterpartyId, overdue: overdue ? 'true' : '', page, pageSize}), {signal}),
  get: (id, signal) => api(`/advances/${id}`, {signal}),
  create: ({operationId, purpose, dueDate}) => api('/advances', {method: 'POST', body: {operationId, purpose, dueDate}}),
  report: (id, lines) => api(`/advances/${id}/report`, {method: 'POST', body: {lines}}),
  settle: (id, operationId) => api(`/advances/${id}/settle`, {method: 'POST', body: operationId ? {operationId} : {}}),
  reportFile: (format, params = {}) => download('/reports/advances.' + format + qs(params), 'Faiza-advances.' + format),
  summary: (params = {}, signal) => api('/reports/advances' + qs(params), {signal})
};

// 4. Купюры и инвентаризация
export const denominations = {
  list: (currency = 'TJS', signal) => api('/denominations' + qs({currency}), {signal}),
  save: (currency, items) => api('/denominations', {method: 'PUT', body: {currency, items}})
};
export const shifts = {
  list: (cashboxId, signal) => api('/shifts' + qs({cashboxId}), {signal}),
  open: ({cashboxId, date, openingBalance}) => api('/shifts/open', {method: 'POST', body: {cashboxId, date, openingBalance}}),
  // Контракт: необязательный пересчёт по купюрам; сумма обязана совпасть с фактическим остатком.
  close: (id, closingBalance, denominationRows) => api(`/shifts/${id}/close`, {method: 'POST', body: {closingBalance, ...(denominationRows ? {denominations: denominationRows} : {})}})
};
export const inventories = {
  list: (cashboxId, signal) => api('/cash-inventories' + qs({cashboxId}), {signal}),
  get: (id, signal) => api(`/cash-inventories/${id}`, {signal}),
  create: ({cashboxId, denominations: rows, commission, note}) => api('/cash-inventories', {method: 'POST', body: {cashboxId, denominations: rows, commission, note: note || undefined}}),
  act: item => download(`/cash-inventories/${item.id}/act.pdf`, `Faiza-inventory-${item.id}.pdf`)
};

// 6. Курсы валют
export const exchangeRates = {
  list: (date, signal) => api('/exchange-rates' + qs({date}), {signal}),
  save: ({currency, date, rateToBase}) => api('/exchange-rates', {method: 'POST', body: {currency, date, rateToBase}})
};

// 7. Dashboard
export const dashboard = (params = {}, signal) => api('/dashboard' + qs(params), {signal});

// 8. Панель администратора
export const adminHealth = signal => api('/admin/health', {signal});

// 9. Логотип и аватары
const imageForm = file => { const form = new FormData(); form.set('file', file); return form; };
export const branding = {
  logoUrl: version => '/api/settings/logo' + (version ? '?v=' + version : ''),
  uploadLogo: file => upload('/settings/logo', imageForm(file), 'PUT'),
  removeLogo: () => api('/settings/logo', {method: 'DELETE'}),
  avatar: (userId, signal) => api(`/users/${userId}/avatar`, {raw: true, signal}),
  uploadAvatar: file => upload('/users/me/avatar', imageForm(file), 'PUT'),
  removeAvatar: () => api('/users/me/avatar', {method: 'DELETE'})
};

// Настройки организации (+ expenseApprovalThreshold)
export const settings = {
  get: signal => api('/settings', {signal}),
  save: body => api('/settings', {method: 'PUT', body})
};
