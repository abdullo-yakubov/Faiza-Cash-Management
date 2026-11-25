// Демо-режим волны 1: моки маршрутов API-контракта (формы JSON — ровно как в контракте).
// Включение: ?demo=1 в адресе, localStorage 'faiza-demo' = '1' или VITE_DEMO=1 при сборке.
// В dev-сборке (npm run dev) моки также подставляются, если маршрут волны 1 вернул 404/405 —
// пока ветка backend волны 1 не слита. В production-сборке без флага моки не используются.
import {today} from '../utils/format.js';
import {addAmounts, subtractAmounts, toCents, fromCents, advanceTotals, denominationTotal} from '../utils/money.js';

const env = (() => { try { return import.meta.env || {}; } catch { return {}; } })();
export function demoEnabled() {
  if (env.VITE_DEMO === '1' || globalThis.__FAIZA_DEMO === true) return true;
  try { return globalThis.localStorage?.getItem('faiza-demo') === '1'; } catch { return false; }
}
export const demoFallbackAllowed = () => env.DEV === true || demoEnabled();
export function setDemo(on) { try { on ? localStorage.setItem('faiza-demo', '1') : localStorage.removeItem('faiza-demo'); } catch { /* недоступно */ } }

const used = new Set();
export function markDemoUsed(path) {
  const key = path.split('?')[0].replace(/\/\d+(?=\/|$)/g, '/{id}');
  if (used.has(key)) return;
  used.add(key);
  try { globalThis.dispatchEvent?.(new CustomEvent('faiza-demo', {detail: [...used]})); } catch { /* SSR / Node */ }
}
export const demoRoutesUsed = () => [...used];

const WAVE1 = [
  /^\/approvals/, /^\/operations\/\d+\/(submit-approval|approve|reject)$/, /^\/attachments/, /^\/advances/, /^\/reports\/advances/,
  /^\/denominations/, /^\/cash-inventories/, /^\/exchange-rates/, /^\/admin\/health/, /^\/settings\/logo/, /^\/users\/(me|[\w-]+)\/avatar/
];
export const isWave1Route = (method, path) => WAVE1.some(re => re.test(path.split('?')[0]));

// ---------- даты ----------
const shift = days => { const d = new Date(today() + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10); };
const at = (days, time = '10:00') => shift(days) + 'T' + time + ':00+05:00';
const nowIso = () => new Date().toISOString();

// ---------- состояние ----------
const people = {
  cashier: {id: 'demo-cashier', fullName: 'Рахимова Зарина'},
  accountant: {id: 'demo-accountant', fullName: 'Назаров Фаррух'},
  manager: {id: 'demo-manager', fullName: 'Шарипов Джамшед'}
};
const employees = [
  {id: 301, name: 'Каримов Далер', kind: 'employee'},
  {id: 302, name: 'Юсупова Мадина', kind: 'employee'},
  {id: 303, name: 'Ахмедов Сухроб', kind: 'employee'}
];
const expenseCategories = [
  {id: 21, name: 'Хозяйственные расходы'}, {id: 22, name: 'Транспортные расходы'}, {id: 23, name: 'Командировочные'},
  {id: 24, name: 'Канцелярские товары'}, {id: 25, name: 'Ремонт и обслуживание'}
];

let seq = 9000;
const nextId = () => ++seq;

const state = {
  threshold: '5000.00',
  operations: [
    {id: 8101, cashboxId: 1, cashbox: 'Основная касса', date: shift(0), type: 'expense', amount: '12500.00', description: 'Оплата ремонта холодильного оборудования склада', categoryId: 25, category: 'Ремонт и обслуживание', counterparty: 'ООО «Сардхона Сервис»', authorId: people.cashier.id, createdBy: people.cashier.fullName, status: 'draft', approvalStatus: 'pending', approvedBy: null, approvedAt: null, approvalComment: null, version: 2},
    {id: 8102, cashboxId: 2, cashbox: 'Касса Худжанд', date: shift(0), type: 'expense', amount: '7800.00', description: 'Закупка упаковочного материала для филиала', categoryId: 21, category: 'Хозяйственные расходы', counterparty: 'ИП Рашидов', authorId: people.accountant.id, createdBy: people.accountant.fullName, status: 'draft', approvalStatus: 'pending', approvedBy: null, approvedAt: null, approvalComment: null, version: 1},
    {id: 8103, cashboxId: 1, cashbox: 'Основная касса', date: shift(-1), type: 'expense', amount: '6400.00', description: 'Командировка в Куляб: проживание и ГСМ', categoryId: 23, category: 'Командировочные', counterparty: 'Каримов Далер', authorId: people.cashier.id, createdBy: people.cashier.fullName, status: 'draft', approvalStatus: 'approved', approvedBy: people.manager.fullName, approvedAt: at(-1, '15:20'), approvalComment: 'Согласовано в пределах сметы', version: 3},
    {id: 8104, cashboxId: 1, cashbox: 'Основная касса', date: shift(-2), type: 'expense', amount: '15000.00', description: 'Покупка офисной мебели', categoryId: 21, category: 'Хозяйственные расходы', counterparty: 'ООО «Мебель Плюс»', authorId: people.cashier.id, createdBy: people.cashier.fullName, status: 'draft', approvalStatus: 'rejected', approvedBy: people.manager.fullName, approvedAt: at(-2, '11:05'), approvalComment: 'Нет счёта поставщика, приложите счёт', version: 2}
  ],
  attachments: [
    {id: 'att-1', ownerType: 'operation', ownerId: 8101, fileName: 'счёт-сардхона-114.pdf', contentType: 'application/pdf', sizeBytes: 182340, sha256: 'a3f1c9e07b5d4e2f8a6b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b3c2d1e0f', uploadedBy: people.cashier.fullName, uploadedAt: at(0, '09:41')},
    {id: 'att-2', ownerType: 'operation', ownerId: 8101, fileName: 'фото-накладной.jpg', contentType: 'image/jpeg', sizeBytes: 954211, sha256: '5e8b1d7a0c9f3e2b4d6a8c0e1f3b5d7a9c1e3f5b7d9a1c3e5f7b9d1a3c5e7f9b', uploadedBy: people.cashier.fullName, uploadedAt: at(0, '09:43')}
  ],
  blobs: new Map(),
  advances: [
    {id: 501, operationId: 7701, documentNumber: 'РКО-000412', cashboxId: 1, cashbox: 'Основная касса', counterpartyId: 301, counterparty: 'Каримов Далер', amount: '3000.00', purpose: 'Командировка в Куляб (2 дня)', issuedAt: shift(-12), dueDate: shift(-5), status: 'issued', lines: []},
    {id: 502, operationId: 7702, documentNumber: 'РКО-000418', cashboxId: 1, cashbox: 'Основная касса', counterpartyId: 302, counterparty: 'Юсупова Мадина', amount: '1500.00', purpose: 'Закупка канцтоваров для бухгалтерии', issuedAt: shift(-3), dueDate: shift(4), status: 'issued', lines: []},
    {id: 503, operationId: 7703, documentNumber: 'РКО-000401', cashboxId: 2, cashbox: 'Касса Худжанд', counterpartyId: 303, counterparty: 'Ахмедов Сухроб', amount: '2000.00', purpose: 'Транспортные расходы по доставке', issuedAt: shift(-9), dueDate: shift(-2), status: 'reported', lines: [
      {id: 1, date: shift(-8), description: 'ГСМ, чек АЗС № 4471', amount: '1240.50', categoryId: 22, category: 'Транспортные расходы'},
      {id: 2, date: shift(-7), description: 'Мойка и стоянка', amount: '980.00', categoryId: 22, category: 'Транспортные расходы'}
    ]},
    {id: 504, operationId: 7690, documentNumber: 'РКО-000377', cashboxId: 1, cashbox: 'Основная касса', counterpartyId: 302, counterparty: 'Юсупова Мадина', amount: '800.00', purpose: 'Почтовые расходы', issuedAt: shift(-20), dueDate: shift(-14), status: 'closed', closedAt: at(-13, '16:00'), settleOperationId: 7695, lines: [
      {id: 3, date: shift(-18), description: 'Отправка документов курьером', amount: '650.00', categoryId: 21, category: 'Хозяйственные расходы'}
    ]}
  ],
  denominations: {
    // Справочник сидирует backend; здесь — демо-копия (источник истины — ответ сервера).
    TJS: [
      ...['500.00', '200.00', '100.00', '50.00', '20.00', '10.00', '5.00', '3.00', '1.00'].map(value => ({value, kind: 'banknote'})),
      ...['5.00', '3.00', '1.00', '0.50', '0.25', '0.20', '0.10', '0.05', '0.01'].map(value => ({value, kind: 'coin'}))
    ]
  },
  inventories: [
    {id: 41, cashboxId: 1, cashbox: 'Основная касса', createdAt: at(-7, '18:10'), createdBy: people.accountant.fullName, denominations: [{value: '200.00', count: 120}, {value: '100.00', count: 85}, {value: '50.00', count: 40}, {value: '20.00', count: 26}, {value: '1.00', count: 18}], commission: ['Назаров Фаррух', 'Шарипов Джамшед', 'Рахимова Зарина'], note: 'Плановая инвентаризация на конец недели', actualBalance: '34038.00', expectedBalance: '34088.00', difference: '-50.00'},
    {id: 40, cashboxId: 2, cashbox: 'Касса Худжанд', createdAt: at(-30, '17:45'), createdBy: people.manager.fullName, denominations: [{value: '100.00', count: 64}, {value: '20.00', count: 15}], commission: ['Шарипов Джамшед', 'Ахмедов Сухроб'], note: null, actualBalance: '6700.00', expectedBalance: '6700.00', difference: '0.00'}
  ],
  rates: [
    {currency: 'USD', date: shift(0), rateToBase: '10.9450'}, {currency: 'EUR', date: shift(0), rateToBase: '11.8210'},
    {currency: 'RUB', date: shift(0), rateToBase: '0.1182'}, {currency: 'USD', date: shift(-1), rateToBase: '10.9520'}
  ],
  logo: null,
  avatar: null
};

// ---------- утилиты ----------
const problem = (status, detail) => { const e = new Error(detail); e.name = 'ApiError'; e.status = status; e.errors = {}; return e; };
const page = (items, query) => {
  const p = Math.max(1, Number(query.get('page')) || 1), size = Math.max(1, Math.min(200, Number(query.get('pageSize')) || 50));
  return {items: items.slice((p - 1) * size, p * size), page: p, pageSize: size, total: items.length};
};
const isOverdue = a => a.status === 'issued' && a.dueDate < today();
const advanceView = a => {
  const totals = advanceTotals(a.amount, a.lines);
  return {...a, overdue: isOverdue(a), spent: a.status === 'issued' ? '0.00' : totals.spent, remainder: a.status === 'issued' ? a.amount : totals.remainder,
    attachments: state.attachments.filter(x => x.ownerType === 'advance' && x.ownerId === a.id)};
};
const textBlob = (text, type) => new Blob([text], {type});
function placeholderImage(name) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="420" viewBox="0 0 600 420"><rect width="600" height="420" fill="#f3ead4"/><rect x="60" y="50" width="480" height="320" rx="12" fill="#fffefa" stroke="#cfc8b7"/><g fill="#cfc8b7"><rect x="100" y="100" width="260" height="14" rx="7"/><rect x="100" y="140" width="400" height="10" rx="5"/><rect x="100" y="170" width="360" height="10" rx="5"/><rect x="100" y="200" width="380" height="10" rx="5"/><rect x="380" y="300" width="120" height="30" rx="6"/></g><text x="300" y="400" font-family="Arial" font-size="16" text-anchor="middle" fill="#625f56">Демо: ${String(name).replace(/[<&>]/g, '')}</text></svg>`;
  return textBlob(svg, 'image/svg+xml');
}
const demoPdf = title => textBlob('%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj 4 0 obj<</Length 60>>stream\nBT /F1 18 Tf 60 780 Td (FAIZA GOLD demo: ' + title.replace(/[()\\]/g, '') + ') Tj ET\nendstream endobj 5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF', 'application/pdf');

function dashboardExtras() {
  const daily = Array.from({length: 30}, (_, i) => {
    const day = 29 - i, seed = (day * 37 + 11) % 17;
    const weekend = new Date(shift(-day) + 'T00:00:00Z').getUTCDay() === 0;
    const income = weekend ? 0 : 18000 + seed * 1450 + (day % 5) * 2300;
    const expense = weekend ? 0 : 9000 + ((seed * 7) % 13) * 1180 + (day % 3) * 1900;
    return {date: shift(-day), income: fromCents(BigInt(income) * 100n), expense: fromCents(BigInt(expense) * 100n)};
  });
  return {
    cashflowDaily: daily,
    branchBalances: [
      {branchId: 'demo-b1', name: 'Душанбе', balance: '184250.40'},
      {branchId: 'demo-b2', name: 'Худжанд', balance: '62310.00'},
      {branchId: 'demo-b3', name: 'Бохтар', balance: '18975.50'}
    ],
    topExpenseCategories: [
      {categoryId: 25, name: 'Ремонт и обслуживание', amount: '48200.00'},
      {categoryId: 22, name: 'Транспортные расходы', amount: '36450.75'},
      {categoryId: 21, name: 'Хозяйственные расходы', amount: '29870.00'},
      {categoryId: 23, name: 'Командировочные', amount: '18600.00'},
      {categoryId: 24, name: 'Канцелярские товары', amount: '7340.20'}
    ],
    pendingApprovals: state.operations.filter(x => x.approvalStatus === 'pending').length,
    overdueAdvances: state.advances.filter(isOverdue).length
  };
}

/** Дополняет реальный ответ сервера полями волны 1, если backend их ещё не отдаёт. */
export function demoAugment(method, path, data) {
  if (!demoFallbackAllowed() || !data || typeof data !== 'object') return data;
  const route = path.split('?')[0];
  if (method === 'GET' && route === '/dashboard' && data.cashflowDaily === undefined) { markDemoUsed('/dashboard (поля волны 1)'); return {...data, ...dashboardExtras()}; }
  if (method === 'GET' && route === '/settings' && !('expenseApprovalThreshold' in data)) { markDemoUsed('/settings (порог согласования)'); return {...data, expenseApprovalThreshold: state.threshold}; }
  if (method === 'GET' && route === '/operations' && Array.isArray(data.items) && data.items.length && !('approvalStatus' in data.items[0])) return {...data, items: data.items.map(x => ({...x, approvalStatus: 'none'}))};
  if (method === 'GET' && /^\/operations\/\d+$/.test(route) && !('approvalStatus' in data)) return {...data, approvalStatus: 'none', approvedBy: null, approvedAt: null, approvalComment: null};
  return data;
}

/** Ответ мока или undefined, если маршрут не относится к демо. */
export async function demoResponse(method, fullPath, body, {raw} = {}) {
  const [path, search = ''] = fullPath.split('?');
  const query = new URLSearchParams(search);
  let m;
  // Порог согласования запоминаем локально, а сам запрос всё равно уходит на сервер.
  if (path === '/settings' && method === 'PUT' && body && 'expenseApprovalThreshold' in body) state.threshold = body.expenseApprovalThreshold || null;
  if (!isWave1Route(method, path)) return undefined;
  if (!globalThis.__FAIZA_DEMO_NO_DELAY) await new Promise(r => setTimeout(r, 180)); // имитация сети — видны скелетоны

  // 1. Согласование
  if (path === '/approvals' && method === 'GET') {
    const status = query.get('status') || 'pending', box = query.get('cashboxId');
    return page(state.operations.filter(x => x.approvalStatus === status && (!box || String(x.cashboxId) === box)), query);
  }
  if ((m = /^\/operations\/(\d+)\/(submit-approval|approve|reject)$/.exec(path)) && method === 'POST') {
    const id = Number(m[1]);
    let op = state.operations.find(x => x.id === id);
    if (!op) { op = {id, approvalStatus: 'none', status: 'draft', type: 'expense'}; }
    if (m[2] === 'submit-approval') { if (op.status !== 'draft' || op.type !== 'expense') throw problem(409, 'На согласование отправляется только черновик расхода.'); Object.assign(op, {approvalStatus: 'pending', approvedBy: null, approvedAt: null, approvalComment: null}); }
    if (m[2] === 'approve') { if (op.approvalStatus !== 'pending') throw problem(409, 'Документ не ожидает согласования.'); Object.assign(op, {approvalStatus: 'approved', approvedBy: people.manager.fullName, approvedAt: nowIso(), approvalComment: body?.comment || null}); }
    if (m[2] === 'reject') { if (!body?.reason?.trim()) throw problem(400, 'Укажите причину отклонения.'); Object.assign(op, {approvalStatus: 'rejected', approvedBy: people.manager.fullName, approvedAt: nowIso(), approvalComment: body.reason.trim()}); }
    op.version = (op.version || 1) + 1;
    return {...op};
  }

  // 2. Вложения
  if (path === '/attachments' && method === 'GET') return state.attachments.filter(x => x.ownerType === query.get('ownerType') && String(x.ownerId) === query.get('ownerId'));
  if (path === '/attachments' && method === 'POST') {
    const file = body?.get?.('file'), ownerType = body?.get?.('ownerType'), ownerId = Number(body?.get?.('ownerId'));
    if (!file) throw problem(400, 'Файл не передан.');
    if (file.size > 5 * 1024 * 1024) throw problem(400, 'Размер файла превышает 5 МБ.');
    if (state.attachments.filter(x => x.ownerType === ownerType && x.ownerId === ownerId).length >= 10) throw problem(409, 'К документу можно приложить не больше 10 файлов.');
    const item = {id: 'att-' + nextId(), ownerType, ownerId, fileName: file.name, contentType: file.type || 'application/octet-stream', sizeBytes: file.size, sha256: 'demo' + String(nextId()).padStart(60, '0'), uploadedBy: 'Вы (демо)', uploadedAt: nowIso()};
    state.attachments.push(item); state.blobs.set(item.id, file);
    return item;
  }
  if ((m = /^\/attachments\/([\w-]+)\/content$/.exec(path)) && method === 'GET') {
    const item = state.attachments.find(x => x.id === m[1]);
    if (!item) throw problem(404, 'Вложение не найдено.');
    return state.blobs.get(item.id) || (item.contentType === 'application/pdf' ? demoPdf(item.fileName) : placeholderImage(item.fileName));
  }
  if ((m = /^\/attachments\/([\w-]+)$/.exec(path)) && method === 'DELETE') {
    const item = state.attachments.find(x => x.id === m[1]);
    if (!item) throw problem(404, 'Вложение не найдено.');
    const owner = item.ownerType === 'operation' ? state.operations.find(x => x.id === item.ownerId) : null;
    if (owner && owner.status !== 'draft') throw problem(409, 'Документ проведён: вложения удалять нельзя.');
    state.attachments = state.attachments.filter(x => x !== item); state.blobs.delete(item.id);
    return null;
  }

  // 3. Авансы
  if (path === '/advances' && method === 'GET') {
    const status = query.get('status'), party = query.get('counterpartyId'), overdue = query.get('overdue') === 'true';
    return page(state.advances.map(advanceView).filter(a => (!status || a.status === status) && (!party || String(a.counterpartyId) === party) && (!overdue || a.overdue)), query);
  }
  if (path === '/advances' && method === 'POST') {
    if (!body?.operationId) throw problem(400, 'Выберите проведённый РКО на сотрудника.');
    if (!body.purpose?.trim()) throw problem(400, 'Укажите цель аванса.');
    const employee = employees[nextId() % employees.length];
    const item = {id: nextId(), operationId: Number(body.operationId), documentNumber: 'РКО-' + String(body.operationId).padStart(6, '0'), cashboxId: 1, cashbox: 'Основная касса', counterpartyId: employee.id, counterparty: employee.name, amount: body.amount || '1000.00', purpose: body.purpose.trim(), issuedAt: today(), dueDate: body.dueDate, status: 'issued', lines: []};
    state.advances.unshift(item);
    return advanceView(item);
  }
  if ((m = /^\/advances\/(\d+)$/.exec(path)) && method === 'GET') {
    const a = state.advances.find(x => x.id === Number(m[1])); if (!a) throw problem(404, 'Аванс не найден.');
    return advanceView(a);
  }
  if ((m = /^\/advances\/(\d+)\/report$/.exec(path)) && method === 'POST') {
    const a = state.advances.find(x => x.id === Number(m[1])); if (!a) throw problem(404, 'Аванс не найден.');
    if (a.status !== 'issued') throw problem(409, 'Отчёт уже принят.');
    if (!body?.lines?.length) throw problem(400, 'Добавьте хотя бы одну строку отчёта.');
    a.lines = body.lines.map((line, i) => ({id: nextId() + i, ...line, category: expenseCategories.find(c => c.id === Number(line.categoryId))?.name || 'Категория'}));
    a.status = 'reported';
    return advanceView(a);
  }
  if ((m = /^\/advances\/(\d+)\/settle$/.exec(path)) && method === 'POST') {
    const a = state.advances.find(x => x.id === Number(m[1])); if (!a) throw problem(404, 'Аванс не найден.');
    if (a.status !== 'reported') throw problem(409, 'Закрыть можно только аванс с принятым отчётом.');
    const {remainder} = advanceView(a);
    if (toCents(remainder) !== 0n && !body?.operationId) throw problem(400, toCents(remainder) > 0n ? 'Нужен проведённый ПКО от сотрудника на сумму остатка.' : 'Нужен проведённый РКО на сумму перерасхода.');
    Object.assign(a, {status: 'closed', closedAt: nowIso(), settleOperationId: body?.operationId || null});
    return advanceView(a);
  }
  if (path === '/reports/advances' && method === 'GET') {
    const rows = state.advances.map(advanceView);
    return {items: rows, summary: {issued: addAmounts(rows.filter(r => r.status !== 'closed').map(r => r.amount)), overdueCount: rows.filter(r => r.overdue).length, count: rows.length}};
  }
  if (path === '/reports/advances.csv') return textBlob('﻿Номер;Сотрудник;Сумма;Срок;Статус\n' + state.advances.map(a => [a.id, a.counterparty, a.amount, a.dueDate, a.status].join(';')).join('\n'), 'text/csv');
  if (path === '/reports/advances.pdf') return demoPdf('Advances report');

  // 4. Купюры и инвентаризация
  if (path === '/denominations' && method === 'GET') return state.denominations[query.get('currency') || 'TJS'] || [];
  if (path === '/denominations' && method === 'PUT') { state.denominations[body?.currency || 'TJS'] = body?.items || []; return state.denominations[body?.currency || 'TJS']; }
  if (path === '/cash-inventories' && method === 'GET') { const box = query.get('cashboxId'); return page(state.inventories.filter(x => !box || String(x.cashboxId) === box), query); }
  if (path === '/cash-inventories' && method === 'POST') {
    if (!body?.cashboxId) throw problem(400, 'Выберите кассу.');
    if (!body.commission?.filter(Boolean).length) throw problem(400, 'Укажите состав комиссии.');
    const actual = denominationTotal(body.denominations || []).total, expected = '34088.00';
    const item = {id: nextId(), cashboxId: Number(body.cashboxId), cashbox: Number(body.cashboxId) === 2 ? 'Касса Худжанд' : 'Основная касса', createdAt: nowIso(), createdBy: 'Вы (демо)', denominations: (body.denominations || []).filter(x => Number(x.count) > 0), commission: body.commission.filter(Boolean), note: body.note || null, actualBalance: actual, expectedBalance: expected, difference: subtractAmounts(actual, expected)};
    state.inventories.unshift(item);
    return item;
  }
  if ((m = /^\/cash-inventories\/(\d+)$/.exec(path)) && method === 'GET') { const x = state.inventories.find(i => i.id === Number(m[1])); if (!x) throw problem(404, 'Акт не найден.'); return x; }
  if (/^\/cash-inventories\/\d+\/act\.pdf$/.test(path)) return demoPdf('Inventory act');

  // 6. Курсы валют
  if (path === '/exchange-rates' && method === 'GET') { const date = query.get('date'); return state.rates.filter(r => !date || r.date === date); }
  if (path === '/exchange-rates' && method === 'POST') {
    if (!/^\d+(\.\d{1,6})?$/.test(String(body?.rateToBase || ''))) throw problem(400, 'Курс — положительное число, до 6 знаков после точки.');
    state.rates = state.rates.filter(r => !(r.currency === body.currency && r.date === body.date));
    const item = {currency: body.currency, date: body.date, rateToBase: body.rateToBase};
    state.rates.unshift(item); return item;
  }

  // 8. Панель администратора
  if (path === '/admin/health' && method === 'GET') return {
    database: {ok: true, version: '18.3', pendingMigrations: 0},
    lastBackup: {at: at(0, '03:00'), status: 'ok', verified: true},
    storage: {attachmentsBytes: 48_234_112, freeDiskBytes: 41_872_553_984},
    activeSessions: 6,
    failedLogins24h: 3,
    usersWithoutCashAccess: [{id: 'demo-u7', fullName: 'Олимова Нигина'}, {id: 'demo-u9', fullName: 'Давлатов Бахтиёр'}],
    appVersion: '1.2.0-wave1-demo'
  };

  // 9. Логотип и аватар
  if (path === '/settings/logo') {
    if (method === 'GET') { if (!state.logo) throw problem(404, 'Логотип не загружен.'); return state.logo; }
    if (method === 'PUT') { state.logo = body?.get?.('file') || null; return null; }
    if (method === 'DELETE') { state.logo = null; return null; }
  }
  if ((m = /^\/users\/(me|[\w-]+)\/avatar$/.exec(path))) {
    if (method === 'GET') { if (!state.avatar) throw problem(404, 'Аватар не загружен.'); return state.avatar; }
    if (method === 'PUT') { state.avatar = body?.get?.('file') || null; return null; }
    if (method === 'DELETE') { state.avatar = null; return null; }
  }
  void raw;
  return undefined;
}

export const demoData = {people, employees, expenseCategories};
