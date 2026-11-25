const TZ = 'Asia/Dushanbe'

export function money(value, currency) {
  const n = Number(value ?? 0)
  const text = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n)
  return currency ? `${text} ${currency}` : text
}

/** Разбирает сумму, введённую пользователем: «1 250,5» → 1250.5. Возвращает null, если сумма неверная. */
export function parseAmount(input) {
  if (input === null || input === undefined) return null
  const cleaned = String(input).replace(/[\s ]/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null
  const n = Number(cleaned)
  return n > 0 ? n : null
}

export function dateTime(iso) {
  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: TZ, day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  }).format(new Date(iso))
}

export function time(iso) {
  return new Intl.DateTimeFormat('ru-RU', { timeZone: TZ, hour: '2-digit', minute: '2-digit' }).format(new Date(iso))
}

/** Сегодняшняя дата в Душанбе в формате ГГГГ-ММ-ДД. */
export function today(now = new Date()) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TZ }).format(now)
}

export const kindNames = {
  Income: 'Приход',
  Expense: 'Расход',
  TransferIn: 'Перевод (получено)',
  TransferOut: 'Перевод (отправлено)',
  Reversal: 'Сторно',
}

export const roleNames = {
  Admin: 'Администратор',
  Accountant: 'Бухгалтер',
  Cashier: 'Кассир',
  Manager: 'Руководитель',
}

export const counterpartyKinds = {
  Customer: 'Покупатель',
  Supplier: 'Поставщик',
  Employee: 'Сотрудник',
  Other: 'Прочее',
}
