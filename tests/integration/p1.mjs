// P1 regression suite (V7.1): rate limits and login timing (D-06/D-07), shift/representative reports,
// CSV exports, cancellation with audit, bounded lists, Dashboard "attention" block.
// Run from faiza-cash: node tests/integration/p1.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {startApi, suiteResults, businessDate, addDays, postOperation, csvRows} from './harness.mjs';

const {test, finish} = suiteResults('p1');
let api;
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
try {
  api = await startApi({port: 5087, name: 'p1'});
  const today = businessDate(), from = addDays(today, -3);
  const income = await api.category('income'), expense = await api.category('expense');
  const party = await api.counterparty('P1 client ' + api.suffix);
  const box = (await api.cashbox('P1')).id, second = (await api.cashbox('P1B', 2)).id;
  const accountant = await api.user('accountant'), representative = await api.user('manager'), cashier = await api.user('cashier');
  for (const user of [accountant, representative, cashier]) await api.expect('/access', {method: 'POST', body: {userId: user.id, cashboxId: box}}, 204);
  await api.expect(`/cashboxes/${box}/opening`, {method: 'POST', body: {date: from, amount: '500.00'}}, 201);
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: box, date: from, openingBalance: '500.00'}}, 201);
  await postOperation(api, {cashboxId: box, date: from, type: 'income', amount: '100.00', categoryId: income});
  const pastShift = (await api.expect(`/shifts?cashboxId=${box}&date=${from}`))[0];
  await api.expect(`/shifts/${pastShift.id}/close`, {method: 'POST', body: {closingBalance: '590.00'}});
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: box, date: today, openingBalance: '600.00'}}, 201);
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: second, date: today, openingBalance: '0.00'}}, 201);
  await postOperation(api, {cashboxId: box, date: today, type: 'expense', amount: '40.00', categoryId: expense});
  await api.expect('/transfers', {method: 'POST', body: {sourceCashboxId: box, targetCashboxId: second, date: today, amount: '60.00', description: 'Перевод P1', requestKey: crypto.randomUUID()}}, 201);

  await test('D-07 unknown login costs the same password-hash work as a wrong password', async () => {
    const known = [], unknown = [];
    for (let i = 0; i < 3; i++) {
      const user = await api.user('auditor');
      let started = performance.now();
      assert.equal((await api.request('/auth/login', {method: 'POST', token: null, body: {login: user.login, password: 'WrongPassword123'}})).status, 401);
      known.push(performance.now() - started);
      started = performance.now();
      assert.equal((await api.request('/auth/login', {method: 'POST', token: null, body: {login: 'nobody-' + crypto.randomUUID().slice(0, 8), password: 'WrongPassword123'}})).status, 401);
      unknown.push(performance.now() - started);
    }
    assert.ok(median(unknown) >= median(known) * 0.6, `unknown ${median(unknown).toFixed(0)} ms vs known ${median(known).toFixed(0)} ms`);
  });

  await test('D-06 change-password and refresh are rate limited with 429', async () => {
    const target = await api.user('auditor');
    const statuses = [];
    for (let i = 0; i < 25; i++) statuses.push((await api.request('/auth/change-password', {method: 'POST', token: target.token, body: {oldPassword: 'not-the-password-1', newPassword: 'NewPassword12345'}})).status);
    assert.equal(statuses[0], 400); assert.ok(statuses.includes(429), statuses.join(','));
    const refresh = [];
    for (let i = 0; i < 25; i++) refresh.push((await api.request('/auth/refresh', {method: 'POST', token: null})).status);
    assert.ok(refresh.includes(429), refresh.join(','));
  });

  await test('Shift report: JSON, CSV and PDF with expected balance, discrepancy and cashbox scope', async () => {
    const q = new URLSearchParams({cashboxId: box, from, to: today});
    const report = await api.expect('/reports/shifts?' + q, {token: accountant.token});
    const closed = report.items.find(x => x.id === pastShift.id);
    assert.equal(closed.expectedBalance, '600.00'); assert.equal(closed.closingBalance, '590.00'); assert.equal(closed.closingDifference, '-10.00');
    assert.equal(report.summary.discrepancyTotal, '-10.00'); assert.equal(report.summary.closed, 1); assert.equal(report.summary.open, 1);
    const csv = csvRows(await (await api.request('/reports/shifts.csv?' + q, {token: accountant.token})).text());
    assert.ok(csv.some(row => row.includes('-10.00')));
    const pdf = await api.request('/reports/shifts.pdf?' + q, {token: accountant.token});
    assert.equal(pdf.status, 200); assert.equal(Buffer.from(await pdf.arrayBuffer()).subarray(0, 4).toString(), '%PDF');
    assert.equal((await api.request('/reports/shifts?' + new URLSearchParams({cashboxId: second, from, to: today}), {token: accountant.token})).status, 403);
    assert.ok((await api.expect('/audit?action=report_exported&entity=shift_report&pageSize=10')).total >= 1);
  });

  let receipt;
  await test('Representative report: totals per status and representative, CSV and PDF', async () => {
    const body = {representativeId: representative.id, counterpartyId: party, basisType: 'order', basisReference: 'P1-R1', amount: '70.00', cashboxId: box, requestKey: crypto.randomUUID()};
    receipt = await api.expect('/representative-receipts', {method: 'POST', token: accountant.token, body}, 201);
    const handed = await api.expect('/representative-receipts', {method: 'POST', token: accountant.token, body: {...body, basisReference: 'P1-R2', amount: '30.00', requestKey: crypto.randomUUID()}}, 201);
    await api.expect(`/representative-receipts/${handed.id}/handover`, {method: 'POST', token: accountant.token});
    const report = await api.expect(`/reports/representatives?from=${from}&to=${today}`, {token: accountant.token});
    const row = report.items.find(x => x.representativeId === representative.id);
    assert.equal(row.representative, representative.fullName);
    assert.equal(row.collected, '70.00'); assert.equal(row.handedOver, '30.00'); assert.equal(row.accepted, '0.00'); assert.equal(row.count, 2);
    const csv = await (await api.request(`/reports/representatives.csv?from=${from}&to=${today}`, {token: accountant.token})).text();
    assert.match(csv, /70\.00/);
    const pdf = await api.request(`/reports/representatives.pdf?from=${from}&to=${today}`, {token: accountant.token});
    assert.equal(pdf.status, 200);
    assert.equal((await api.request(`/reports/representatives?from=${from}&to=${today}`, {token: cashier.token})).status, 403);
  });

  await test('CSV exports for settlements and transfers neutralise formulas and match the data', async () => {
    await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: box, direction: 'receivable', basisType: 'manual', basisReference: '=HYPERLINK("x")', totalAmount: '15.00', dueDate: today}}, 201);
    const settlements = await (await api.request('/reports/settlements.csv?q=' + encodeURIComponent('HYPERLINK'))).text();
    assert.match(settlements, /"'=HYPERLINK/); assert.match(settlements, /15\.00/);
    const transfers = csvRows(await (await api.request(`/reports/transfers.csv?cashboxId=${box}&from=${today}&to=${today}`)).text());
    assert.ok(transfers.some(row => row.includes('60.00')));
  });

  await test('Cancelling a receipt and an unpaid debt is audited; accepted receipts and paid debts cannot be cancelled', async () => {
    await api.expect(`/representative-receipts/${receipt.id}/cancel`, {method: 'POST', token: accountant.token, body: {reason: ''}}, 400);
    const cancelled = await api.expect(`/representative-receipts/${receipt.id}/cancel`, {method: 'POST', token: accountant.token, body: {reason: 'Ошибочная регистрация'}});
    assert.equal(cancelled.status, 'cancelled');
    await api.expect(`/representative-receipts/${receipt.id}/cancel`, {method: 'POST', token: accountant.token, body: {reason: 'Повтор'}}, 409);
    const debt = await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: box, direction: 'payable', basisType: 'manual', basisReference: 'CANCEL-' + api.suffix, totalAmount: '25.00', dueDate: today}}, 201);
    await api.expect(`/settlements/${debt.id}/cancel`, {method: 'POST', token: cashier.token, body: {reason: 'Нет прав'}}, 403);
    const result = await api.expect(`/settlements/${debt.id}/cancel`, {method: 'POST', token: accountant.token, body: {reason: 'Дубликат'}});
    assert.equal(result.status, 'cancelled');
    const listed = (await api.expect('/settlements?status=cancelled')).find(x => x.id === debt.id);
    assert.equal(listed.remaining, '0.00');
    const paid = await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: box, direction: 'receivable', basisType: 'manual', basisReference: 'PAID-' + api.suffix, totalAmount: '10.00', dueDate: today}}, 201);
    const payment = await postOperation(api, {cashboxId: box, date: today, type: 'income', amount: '10.00', categoryId: income, counterpartyId: party});
    await api.expect(`/settlements/${paid.id}/allocate`, {method: 'POST', body: {operationId: payment.id, amount: '4.00'}});
    await api.expect(`/settlements/${paid.id}/cancel`, {method: 'POST', body: {reason: 'Поздно'}}, 409);
    await api.expect(`/settlements/${debt.id}/allocate`, {method: 'POST', body: {operationId: payment.id, amount: '1.00'}}, 409);
    for (const action of ['representative_receipt_cancelled', 'settlement_cancelled']) assert.ok((await api.expect(`/audit?action=${action}&pageSize=5`)).total >= 1, action);
    const report = await api.expect('/reports/settlements?status=cancelled');
    assert.ok(report.items.some(x => x.id === debt.id));
  });

  await test('Lists are bounded: pageSize is capped and X-Total-Count reports the full size', async () => {
    await api.counterparty('P1 second client ' + api.suffix);
    for (const path of ['/settlements', '/representative-receipts', '/categories', '/counterparties', '/access', '/branches']) {
      const one = await api.request(path + '?page=1&pageSize=1');
      assert.equal(one.status, 200, path);
      const rows = await one.json();
      assert.ok(Array.isArray(rows) && rows.length === 1, `${path} returns one row`);
      assert.ok(Number(one.headers.get('x-total-count')) >= 2, `${path} X-Total-Count`);
      const huge = await api.request(path + '?pageSize=100000');
      assert.ok((await huge.json()).length <= 500, path + ' cap');
    }
  });

  await test('Dashboard "attention" block is built from existing data: shift discrepancy, overdue debt', async () => {
    const overdue = addDays(today, -1);
    await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: box, direction: 'receivable', basisType: 'order', basisReference: 'LATE-' + api.suffix, totalAmount: '5.00', dueDate: overdue}}, 201);
    const dashboard = await api.expect('/dashboard?' + new URLSearchParams({cashboxId: box, from, to: today}), {token: accountant.token});
    const codes = dashboard.attention.map(x => x.code);
    assert.ok(codes.includes('shift_discrepancy'), codes.join(','));
    assert.ok(codes.includes('overdue_debt'), codes.join(','));
    const discrepancy = dashboard.attention.find(x => x.code === 'shift_discrepancy');
    assert.equal(discrepancy.severity, 'warning'); assert.equal(discrepancy.amount, '-10.00'); assert.ok(discrepancy.href);
    const cashierView = await api.expect('/dashboard?' + new URLSearchParams({cashboxId: box, from, to: today}), {token: cashier.token});
    assert.ok(!cashierView.attention.some(x => x.code === 'overdue_debt'), 'cashier does not see debts');
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
