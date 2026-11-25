// FG7-002: financial invariants on one synthetic PostgreSQL dataset.
// Dashboard = cashbook = reports = closed shifts = independent SQL oracle; payments <= debt; transfer is -X/+X.
// Run from faiza-cash: node tests/integration/invariants.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {startApi, suiteResults, businessDate, addDays, postOperation, csvRows} from './harness.mjs';

const {test, finish} = suiteResults('invariants');
let api;
try {
  api = await startApi({port: 5085, name: 'invariants'});
  const today = businessDate(), past = addDays(today, -2), from = addDays(today, -7);
  const income = await api.category('income'), expense = await api.category('expense');
  const party = await api.counterparty('Invariant client ' + api.suffix);
  const branchA = (await api.expect('/branches', {method: 'POST', body: {code: 'IA' + api.suffix, name: 'Invariant A', city: 'Душанбе'}}, 201)).id;
  const branchB = (await api.expect('/branches', {method: 'POST', body: {code: 'IB' + api.suffix, name: 'Invariant B', city: 'Худжанд'}}, 201)).id;
  const d1 = (await api.cashbox('INV1', branchA)).id, d2 = (await api.cashbox('INV2', branchB)).id, d3 = (await api.cashbox('INV3', branchA)).id;
  const boxes = [d1, d2, d3];
  const oracle = (box, date = today) => api.scalar(`SELECT (COALESCE((SELECT "Amount" FROM "Openings" WHERE "CashboxId"=${box} AND "Date"<='${date}'),0)+COALESCE((SELECT SUM(CASE WHEN "Type"='income' THEN "Amount" ELSE -"Amount" END) FROM "Operations" WHERE "CashboxId"=${box} AND "Status"='posted' AND "Date"<='${date}'),0))::numeric(18,2)`);
  const balance = async (box, date = today) => (await api.expect(`/cashboxes/${box}/balance?date=${date}`)).balance;
  const openShift = async (box, date) => api.expect('/shifts/open', {method: 'POST', body: {cashboxId: box, date, openingBalance: await balance(box, date)}}, 201);
  const closeShift = async (box, date, closingBalance) => {
    const shift = (await api.expect(`/shifts?cashboxId=${box}&date=${date}`)).find(x => x.status === 'open');
    return api.expect(`/shifts/${shift.id}/close`, {method: 'POST', body: {closingBalance: closingBalance ?? await balance(box, date)}});
  };

  // Synthetic dataset
  await api.expect(`/cashboxes/${d1}/opening`, {method: 'POST', body: {date: addDays(today, -5), amount: '2000.00'}}, 201);
  await api.expect(`/cashboxes/${d2}/opening`, {method: 'POST', body: {date: addDays(today, -3), amount: '300.00'}}, 201);
  await openShift(d1, past); await openShift(d2, past);
  await postOperation(api, {cashboxId: d1, date: past, type: 'income', amount: '500.00', categoryId: income});
  await postOperation(api, {cashboxId: d1, date: past, type: 'expense', amount: '120.00', categoryId: expense});
  await closeShift(d1, past); await closeShift(d2, past, '290.00');
  for (const box of boxes) await openShift(box, today);
  await postOperation(api, {cashboxId: d3, date: today, type: 'income', amount: '100.00', categoryId: income});
  const reversible = await postOperation(api, {cashboxId: d2, date: today, type: 'income', amount: '40.00', categoryId: income});
  const reversal = await api.expect(`/operations/${reversible.id}/reversals`, {method: 'POST', body: {reason: 'Ошибочный документ', date: today}}, 201);
  await api.expect(`/operations/${reversal.id}/post`, {method: 'POST', body: {expectedVersion: reversal.version}});
  const beforeTransfer = {source: await balance(d1), target: await balance(d3)};
  const transfer = await api.expect('/transfers', {method: 'POST', body: {sourceCashboxId: d1, targetCashboxId: d3, date: today, amount: '250.00', description: 'Подкрепление кассы', requestKey: crypto.randomUUID()}}, 201);
  const settlement = await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: d1, direction: 'receivable', basisType: 'order', basisReference: 'INV-' + api.suffix, totalAmount: '600.00', dueDate: today}}, 201);
  for (const amount of ['200.00', '150.00']) {
    const payment = await postOperation(api, {cashboxId: d1, date: today, type: 'income', amount, categoryId: income, counterpartyId: party});
    await api.expect(`/settlements/${settlement.id}/allocate`, {method: 'POST', body: {operationId: payment.id, amount}});
  }
  for (const box of boxes) await closeShift(box, today);

  await test('Every cashbox: SQL oracle = balance API = dashboard = trend = cashbook = summary = CSV', async () => {
    for (const box of boxes) {
      const expected = oracle(box);
      const q = new URLSearchParams({cashboxId: box, from, to: today});
      const dashboard = await api.expect('/dashboard?' + q);
      const csv = csvRows(await (await api.request('/reports/cash.csv?' + q)).text());
      const values = {
        api: await balance(box), dashboard: dashboard.balances.find(x => x.id === box).balance, trend: dashboard.balanceTrend.at(-1).balance,
        cashbook: (await api.expect('/reports/cashbook?' + q)).closing, summary: (await api.expect('/summary?' + q)).closing, csv: csv.at(-1).at(-1)
      };
      for (const [source, value] of Object.entries(values)) assert.equal(value, expected, `cashbox ${box}: ${source}=${value}, oracle=${expected}`);
    }
  });

  await test('Dashboard total and branch reports equal the sum of cashbox balances', async () => {
    const all = await api.expect('/dashboard?' + new URLSearchParams({from, to: today}));
    const ours = all.balances.filter(x => boxes.includes(x.id)).reduce((sum, x) => sum + Number(x.balance), 0);
    assert.equal(ours.toFixed(2), boxes.reduce((sum, box) => sum + Number(oracle(box)), 0).toFixed(2));
    for (const [branch, members] of [[branchA, [d1, d3]], [branchB, [d2]]]) {
      const expected = members.reduce((sum, box) => sum + Number(oracle(box)), 0).toFixed(2);
      const csv = csvRows(await (await api.request(`/reports/branch.csv?branchId=${branch}&from=${from}&to=${today}`)).text());
      assert.equal(csv.at(-1).at(-1), expected, `branch ${branch}`);
      assert.equal((await api.expect('/dashboard?' + new URLSearchParams({branchId: branch, from, to: today}))).totalBalance, expected);
    }
  });

  await test('Closed shifts reconcile: closing balance minus difference equals the calculated balance', async () => {
    let checked = 0;
    for (const box of boxes) {
      for (const shift of (await api.expect(`/shifts?cashboxId=${box}`)).filter(x => x.status === 'closed')) {
        assert.equal((Number(shift.closingBalance) - Number(shift.closingDifference)).toFixed(2), oracle(box, shift.date), `shift ${shift.id}`);
        checked++;
      }
    }
    assert.equal(checked, 5);
    const discrepancy = (await api.expect(`/shifts?cashboxId=${d2}&date=${past}`))[0];
    assert.equal(discrepancy.closingDifference, '-10.00');
  });

  await test('Payments never exceed the debt and history sums to the paid amount', async () => {
    const row = (await api.expect('/settlements?counterpartyId=' + party)).find(x => x.id === settlement.id);
    assert.equal(row.paidAmount, '350.00'); assert.equal(row.remaining, '250.00'); assert.equal(row.status, 'partially_paid');
    const history = await api.expect(`/settlements/${settlement.id}/history`);
    assert.equal(history.reduce((sum, x) => sum + Number(x.amount), 0).toFixed(2), row.paidAmount);
    assert.equal(api.scalar('SELECT count(*) FROM "Settlements" WHERE "PaidAmount">"TotalAmount" OR "PaidAmount"<0'), '0');
    assert.equal(api.scalar('SELECT count(*) FROM "Settlements" s WHERE s."PaidAmount"<>COALESCE((SELECT SUM("Amount") FROM "PaymentAllocations" a WHERE a."SettlementId"=s."Id"),0)'), '0');
  });

  await test('Transfer creates exactly -X on the source and +X on the target', async () => {
    const pair = api.scalar(`SELECT string_agg("CashboxId"||':'||"Type"||':'||"Amount"||':'||"Status", ',' ORDER BY "Type") FROM "Operations" WHERE "TransferId"=${transfer.id}`);
    assert.equal(pair, `${d1}:expense:250.00:posted,${d3}:income:250.00:posted`);
    const sourceAfter = api.scalar(`SELECT (${beforeTransfer.source}-250)::numeric(18,2)`), targetAfter = api.scalar(`SELECT (${beforeTransfer.target}+250)::numeric(18,2)`);
    const transferDayDelta = box => api.scalar(`SELECT COALESCE(SUM(CASE WHEN "Type"='income' THEN "Amount" ELSE -"Amount" END),0)::numeric(18,2) FROM "Operations" WHERE "CashboxId"=${box} AND "TransferId"=${transfer.id}`);
    assert.equal(transferDayDelta(d1), '-250.00'); assert.equal(transferDayDelta(d3), '250.00');
    assert.equal(oracle(d1), (Number(sourceAfter) + 350).toFixed(2), 'source = before - transfer + later settlement payments');
    assert.equal(oracle(d3), targetAfter);
  });

  await test('Reversal nets the original document to zero and keeps both records', async () => {
    assert.equal(oracle(d2), '300.00');
    assert.equal(api.scalar(`SELECT count(*) FROM "Operations" WHERE "Id" IN (${reversible.id},${reversal.id}) AND "Status"='posted'`), '2');
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
