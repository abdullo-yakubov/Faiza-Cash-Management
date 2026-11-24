// Денежная арифметика клиента без float: все расчёты — в целых дирамах (BigInt).
// Сервер передаёт суммы строками ("1500.00"); здесь они разбираются, складываются
// и форматируются обратно в ту же строковую форму.

const AMOUNT = /^(-?)(\d+)(?:[.,](\d{1,2}))?$/;

/** Строка-сумма → целые дирамы (BigInt). Пустое значение → 0n. Неверный формат → ошибка. */
export function toCents(value) {
  if (value === null || value === undefined || value === '') return 0n;
  if (typeof value === 'bigint') return value;
  const text = String(value).trim().replace(/[\s  ]/g, '');
  const match = AMOUNT.exec(text);
  if (!match) throw new Error('Сумма в неверном формате: ' + text);
  const cents = BigInt(match[2]) * 100n + BigInt((match[3] || '').padEnd(2, '0'));
  return match[1] && cents !== 0n ? -cents : cents;
}

/** Целые дирамы → строка "1234.56" (как в API). */
export function fromCents(cents) {
  const value = BigInt(cents);
  const negative = value < 0n;
  const abs = negative ? -value : value;
  return (negative ? '-' : '') + (abs / 100n) + '.' + String(abs % 100n).padStart(2, '0');
}

/** Безопасный разбор: неверная строка → null вместо исключения. */
export function tryCents(value) {
  try { return toCents(value); } catch { return null; }
}

export const addAmounts = (...values) => fromCents(values.flat().reduce((sum, v) => sum + toCents(v), 0n));
export const subtractAmounts = (a, b) => fromCents(toCents(a) - toCents(b));
export const compareAmounts = (a, b) => { const d = toCents(a) - toCents(b); return d > 0n ? 1 : d < 0n ? -1 : 0; };
export const isZero = value => toCents(value) === 0n;
export const isNegative = value => toCents(value) < 0n;
export const isPositive = value => toCents(value) > 0n;

/** Целое неотрицательное количество из поля ввода; пусто → 0; иначе null. */
export function parseCount(value) {
  const text = String(value ?? '').trim();
  if (text === '') return 0;
  if (!/^\d{1,7}$/.test(text)) return null;
  return Number(text);
}

/** Номинал × количество → дирамы. */
export const lineCents = (denomination, count) => toCents(denomination) * BigInt(count || 0);

/**
 * Итог пересчёта по купюрам.
 * rows: [{ value: "200.00", count: 3 | "3" }]
 * → { total: "600.00", invalid: [индексы строк с неверным количеством], lines: [{value,count,amount}] }
 */
export function denominationTotal(rows = []) {
  const invalid = [];
  let total = 0n;
  const lines = rows.map((row, index) => {
    const count = parseCount(row.count);
    if (count === null) { invalid.push(index); return { value: row.value, count: 0, amount: '0.00' }; }
    const cents = lineCents(row.value, count);
    total += cents;
    return { value: row.value, count, amount: fromCents(cents) };
  });
  return { total: fromCents(total), invalid, lines };
}

/**
 * Сравнение факта с расчётным остатком.
 * → { difference: "-50.00", kind: 'shortage' | 'surplus' | 'match' }
 */
export function reconcile(actual, expected) {
  const diff = toCents(actual) - toCents(expected);
  return { difference: fromCents(diff), kind: diff < 0n ? 'shortage' : diff > 0n ? 'surplus' : 'match' };
}

/**
 * Итог авансового отчёта.
 * → { spent, remainder, overspent: bool, settlement: 'return' | 'reimburse' | 'none' }
 *   return — сотрудник возвращает остаток (нужен ПКО), reimburse — перерасход (нужен РКО).
 */
export function advanceTotals(amount, lines = []) {
  const spent = lines.reduce((sum, line) => sum + (tryCents(line.amount) ?? 0n), 0n);
  const remainder = toCents(amount) - spent;
  return {
    spent: fromCents(spent),
    remainder: fromCents(remainder),
    overspent: remainder < 0n,
    settlement: remainder > 0n ? 'return' : remainder < 0n ? 'reimburse' : 'none'
  };
}

/** Модуль суммы (для подписи «Перерасход 50,00» без минуса). */
export const absAmount = value => { const c = toCents(value); return fromCents(c < 0n ? -c : c); };
