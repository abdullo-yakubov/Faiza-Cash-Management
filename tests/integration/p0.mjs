// P0 regression suite (V7.1): unified balance, opening API, shift reconciliation, business audit,
// responsible representative on debts. Run from faiza-cash: node tests/integration/p0.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {startApi, suiteResults, businessDate, addDays, postOperation, csvRows} from './harness.mjs';

const {test, finish} = suiteResults('p0');
let api;
try {
  api = await startApi({port: 5084, name: 'p0'});
  const to = businessDate(), from = addDays(to, -14), activation = addDays(from, 9);
  const branch = await api.expect('/branches', {method: 'POST', body: {code: 'P0' + api.suffix, name: 'P0 branch ' + api.suffix, city: 'Душанбе'}}, 201);
  const boxA = await api.cashbox('P0A', branch.id), boxB = await api.cashbox('P0B', branch.id);
  const income = await api.category('income'), expense = await api.category('expense');
  const balanceOf = async (id, date = to) => (await api.expect(`/cashboxes/${id}/balance?date=${date}`)).balance;
  const csvLastBalance = async path => csvRows(await (await api.request(path)).text()).at(-1).at(-1);
  const query = (cashboxId, a = from, b = to) => new URLSearchParams({cashboxId, from: a, to: b});

  await test('FG7-001 opening API is admin-only, one-time, validated and audited', async () => {
    const cashier = await api.user('cashier');
    await api.expect(`/cashboxes/${boxA.id}/opening`, {method: 'POST', token: cashier.token, body: {date: from, amount: '1000.00'}}, 403);
    const created = await api.expect(`/cashboxes/${boxA.id}/opening`, {method: 'POST', body: {date: from, amount: '1000.00'}}, 201);
    assert.equal(created.amount, '1000.00');
    await api.expect(`/cashboxes/${boxA.id}/opening`, {method: 'POST', body: {date: from, amount: '5.00'}}, 409);
    await api.expect(`/cashboxes/${boxB.id}/opening`, {method: 'POST', body: {date: activation, amount: '500.005'}}, 400);
    await api.expect(`/cashboxes/${boxB.id}/opening`, {method: 'POST', body: {date: addDays(to, 1), amount: '500.00'}}, 400);
    await api.expect(`/cashboxes/${boxB.id}/opening`, {method: 'POST', body: {date: activation, amount: '500.00'}}, 201);
    const listed = (await api.expect('/cashboxes')).find(x => x.id === boxA.id);
    assert.equal(listed.openingDate, from); assert.equal(listed.openingAmount, '1000.00');
    const audit = await api.expect('/audit?action=cashbox_opening_set&pageSize=100');
    assert.ok(audit.items.some(x => x.entityId === boxA.id), 'cashbox_opening_set must be audited');
  });

  await test('FG7-001 opening on the first day: trend, balance API, cashbook and CSV all show 1000', async () => {
    const dashboard = await api.expect('/dashboard?' + query(boxA.id));
    assert.ok(dashboard.balanceTrend.length === 15 && dashboard.balanceTrend.every(x => x.balance === '1000.00'), JSON.stringify(dashboard.balanceTrend.slice(0, 3)));
    assert.equal(dashboard.balances.find(x => x.id === boxA.id).balance, '1000.00');
    const book = await api.expect('/reports/cashbook?' + query(boxA.id));
    assert.equal(book.opening, '1000.00'); assert.equal(book.closing, '1000.00');
    assert.equal(await csvLastBalance('/reports/cash.csv?' + query(boxA.id)), '1000.00');
    assert.equal(await balanceOf(boxA.id), '1000.00');
  });

  await test('FG7-001 cashbox activated on day 10 is absent before and counted from that day', async () => {
    const dashboard = await api.expect('/dashboard?' + query(boxB.id));
    for (const point of dashboard.balanceTrend) assert.equal(point.balance, point.date < activation ? '0.00' : '500.00', point.date);
    const book = await api.expect('/reports/cashbook?' + query(boxB.id));
    assert.equal(book.opening, '0.00'); assert.equal(book.closing, '500.00');
    assert.equal(await csvLastBalance('/reports/cash.csv?' + query(boxB.id)), '500.00', 'cashbook CSV must include activation inside the period');
    assert.equal(await csvLastBalance(`/reports/branch.csv?branchId=${branch.id}&from=${from}&to=${to}`), '1500.00', 'branch report must include both cashboxes');
    assert.equal(await csvLastBalance(`/reports/branch.csv?branchId=${branch.id}&from=${from}&to=${addDays(activation, -1)}`), '1000.00');
    assert.equal(await csvLastBalance(`/reports/branch.csv?branchId=${branch.id}&from=${activation}&to=${to}`), '1500.00', 'opening dated on the first day belongs to the period start');
    const pdf = await api.request(`/reports/branch.pdf?branchId=${branch.id}&from=${from}&to=${to}`);
    assert.equal(pdf.status, 200); assert.equal(Buffer.from(await pdf.arrayBuffer()).subarray(0, 4).toString(), '%PDF');
  });

  await test('FG7-001 period before activation: cashbox is absent instead of a zero/error row', async () => {
    const before = addDays(activation, -1);
    const dashboard = await api.expect('/dashboard?' + new URLSearchParams({branchId: branch.id, from, to: before}));
    assert.ok(!dashboard.balances.some(x => x.id === boxB.id), 'not yet activated cashbox must be absent');
    assert.ok(dashboard.balances.some(x => x.id === boxA.id));
    assert.equal(dashboard.totalBalance, '1000.00');
    const book = await api.expect('/reports/cashbook?' + query(boxB.id, from, before));
    assert.equal(book.closing, '0.00');
  });

  await test('D-03 shift opening balance must equal the calculated balance', async () => {
    const wrong = await api.request('/shifts/open', {method: 'POST', body: {cashboxId: boxA.id, date: to, openingBalance: '1.00'}});
    assert.equal(wrong.status, 409);
    assert.equal((await wrong.json()).expectedBalance, '1000.00');
    await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: boxA.id, date: to, openingBalance: '1000.00'}}, 201);
  });

  await test('FG7-001 after postings the last trend point equals balance API, cashbook, summary and CSV', async () => {
    await postOperation(api, {cashboxId: boxA.id, date: to, type: 'income', amount: '200.00', categoryId: income});
    await postOperation(api, {cashboxId: boxA.id, date: to, type: 'expense', amount: '50.00', categoryId: expense});
    const expected = '1150.00';
    const dashboard = await api.expect('/dashboard?' + query(boxA.id));
    assert.equal(dashboard.balanceTrend.at(-1).balance, expected);
    assert.equal(await balanceOf(boxA.id), expected);
    assert.equal((await api.expect('/reports/cashbook?' + query(boxA.id))).closing, expected);
    assert.equal((await api.expect('/summary?' + query(boxA.id))).closing, expected);
    assert.equal(await csvLastBalance('/reports/cash.csv?' + query(boxA.id)), expected);
    const byBranch = await api.expect('/dashboard?' + new URLSearchParams({branchId: branch.id, from, to}));
    assert.equal(byBranch.totalBalance, '1650.00');
  });

  await test('D-04 shift, cashbox, settings and backup actions produce business audit events', async () => {
    await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: boxB.id, date: to, openingBalance: '500.00'}}, 201);
    const shift = (await api.expect(`/shifts?cashboxId=${boxB.id}&date=${to}`)).find(x => x.status === 'open');
    await api.expect(`/shifts/${shift.id}/close`, {method: 'POST', body: {closingBalance: '500.00'}});
    await api.expect(`/cashboxes/${boxA.id}`, {method: 'PATCH', body: {balanceLimit: 5000}}, 204);
    await api.expect(`/cashboxes/${boxB.id}`, {method: 'PATCH', body: {active: false}}, 204);
    const settings = await api.expect('/settings');
    await api.expect('/settings', {method: 'PUT', body: {name: settings.name, currency: settings.currency}}, 204);
    const backup = await api.expect('/backups', {method: 'POST'});
    await api.expect(`/backups/${backup.id}/verify`, {method: 'POST'});
    const actions = ['shift_opened', 'shift_closed', 'cashbox_updated', 'cashbox_deactivated', 'settings_updated', 'backup_created', 'backup_verified'];
    const options = await api.expect('/audit/options');
    for (const action of actions) {
      const events = await api.expect(`/audit?action=${action}&pageSize=10`);
      assert.ok(events.total > 0, `missing audit event ${action}`);
      assert.ok(events.items[0].actionName && events.items[0].actionName !== 'Действие пользователя', `missing Russian name for ${action}`);
      assert.ok(options.actions.some(x => x.code === action), `audit filter lacks ${action}`);
    }
  });

  await test('Debt keeps a responsible representative: create, list, filter, report and validation', async () => {
    const accountant = await api.user('accountant');
    const representative = await api.user('manager');
    await api.expect('/access', {method: 'POST', body: {userId: accountant.id, cashboxId: boxA.id}}, 204);
    const candidates = await api.expect('/representatives', {token: accountant.token});
    assert.ok(candidates.some(x => x.id === representative.id && x.fullName));
    assert.ok(candidates.every(x => !('passwordHash' in x) && !('login' in x)), 'representative list exposes only id and name');
    const party = await api.counterparty('Representative client ' + api.suffix);
    const body = {counterpartyId: party, cashboxId: boxA.id, direction: 'receivable', basisType: 'order', basisReference: 'REP-' + api.suffix, totalAmount: '40.00', dueDate: to};
    await api.expect('/settlements', {method: 'POST', token: accountant.token, body: {...body, representativeId: '00000000-0000-0000-0000-000000000001'}}, 400);
    const created = await api.expect('/settlements', {method: 'POST', token: accountant.token, body: {...body, representativeId: representative.id}}, 201);
    await api.expect('/settlements', {method: 'POST', token: accountant.token, body: {...body, basisReference: 'NOREP-' + api.suffix}}, 201);
    const filtered = await api.expect(`/settlements?representativeId=${representative.id}`, {token: accountant.token});
    assert.deepEqual(filtered.map(x => x.id), [created.id]);
    assert.equal(filtered[0].representativeId, representative.id); assert.equal(filtered[0].representative, representative.fullName);
    const report = await api.expect(`/reports/settlements?representativeId=${representative.id}`, {token: accountant.token});
    assert.equal(report.total, 1); assert.equal(report.items[0].representative, representative.fullName);
    const pdf = await api.request(`/reports/settlements.pdf?representativeId=${representative.id}`, {token: accountant.token});
    assert.equal(pdf.status, 200);
  });

  await test('SoD-01 pure admin cannot perform financial operations (403)', async () => {
    const pureAdmin = await api.user('admin');
    const draft = await api.expect('/operations', {method: 'POST', body: {cashboxId: boxA.id, date: to, type: 'income', amount: '1.00', description: 'SoD draft', categoryId: income, counterpartyId: null, documentTypeId: null, requestKey: crypto.randomUUID()}}, 201);
    const as = {token: pureAdmin.token};
    await api.expect('/operations', {...as, method: 'POST', body: {cashboxId: boxA.id, date: to, type: 'income', amount: '1.00', description: 'Admin draft', categoryId: income, counterpartyId: null, documentTypeId: null, requestKey: crypto.randomUUID()}}, 403);
    await api.expect(`/operations/${draft.id}`, {...as, method: 'PATCH', body: {amount: '2.00', version: draft.version}}, 403);
    await api.expect(`/operations/${draft.id}/post`, {...as, method: 'POST', body: {expectedVersion: draft.version}}, 403);
    await api.expect(`/operations/${draft.id}?version=${draft.version}`, {...as, method: 'DELETE'}, 403);
    await api.expect(`/operations/${draft.id}/reversals`, {...as, method: 'POST', body: {reason: 'SoD', date: to}}, 403);
    await api.expect('/shifts/open', {...as, method: 'POST', body: {cashboxId: boxB.id, date: to, openingBalance: '0.00'}}, 403);
    await api.expect('/shifts/1/close', {...as, method: 'POST', body: {closingBalance: '0.00'}}, 403);
    await api.expect('/transfers', {...as, method: 'POST', body: {sourceCashboxId: boxA.id, targetCashboxId: boxB.id, date: to, amount: '1.00', description: 'SoD', requestKey: crypto.randomUUID()}}, 403);
    await api.expect('/representative-receipts/1/accept', {...as, method: 'POST'}, 403);
    await api.expect('/settlements/1/allocate', {...as, method: 'POST', body: {operationId: draft.id, amount: '1.00'}}, 403);
    // Чтение остаётся доступным администратору.
    await api.expect(`/operations/${draft.id}`, as, 200);
    await api.expect('/settlements', as, 200);
    await api.expect(`/operations/${draft.id}?version=${draft.version}`, {method: 'DELETE'}, 204);
  });

  await test('SoD-02 transfers use only system categories, which cannot be renamed or archived', async () => {
    const all = await api.expect('/categories');
    const systemIncome = all.find(x => x.isSystem && x.type === 'income'), systemExpense = all.find(x => x.isSystem && x.type === 'expense');
    assert.equal(systemIncome?.name, 'Межкассовый перевод (приход)');
    assert.equal(systemExpense?.name, 'Межкассовый перевод (расход)');
    await api.expect(`/categories/${systemIncome.id}`, {method: 'PATCH', body: {name: 'Другое имя'}}, 409);
    await api.expect(`/categories/${systemExpense.id}`, {method: 'PATCH', body: {active: false}}, 409);
    await api.expect('/operations', {method: 'POST', body: {cashboxId: boxA.id, date: to, type: 'income', amount: '1.00', description: 'System category misuse', categoryId: systemIncome.id, counterpartyId: null, documentTypeId: null, requestKey: crypto.randomUUID()}}, 400);
    const boxC = await api.cashbox('P0C', branch.id), boxD = await api.cashbox('P0D', branch.id);
    await api.expect(`/cashboxes/${boxC.id}/opening`, {method: 'POST', body: {date: to, amount: '10.00'}}, 201);
    await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: boxC.id, date: to, openingBalance: '10.00'}}, 201);
    await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: boxD.id, date: to, openingBalance: '0.00'}}, 201);
    const transfer = await api.expect('/transfers', {method: 'POST', body: {sourceCashboxId: boxC.id, targetCashboxId: boxD.id, date: to, amount: '1.00', description: 'System categories', requestKey: crypto.randomUUID()}}, 201);
    const source = await api.expect(`/operations/${transfer.sourceOperationId}`), target = await api.expect(`/operations/${transfer.targetOperationId}`);
    assert.equal(source.categoryId, systemExpense.id); assert.equal(target.categoryId, systemIncome.id);
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
