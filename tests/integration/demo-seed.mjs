// FG7-024: runs scripts/seed-demo.mjs against a fresh test API and checks the demo network it builds.
// Run from faiza-cash: node tests/integration/demo-seed.mjs
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {startApi, suiteResults, businessDate, addDays} from './harness.mjs';

const {test, finish} = suiteResults('demo-seed');
let api;
try {
  api = await startApi({port: 5088, name: 'demo-seed'});
  const run = spawnSync(process.execPath, ['scripts/seed-demo.mjs'], {encoding: 'utf8', env: {...process.env, FAIZA_API: api.base, FAIZA_ADMIN_LOGIN: api.adminLogin, FAIZA_ADMIN_PASSWORD: api.adminPassword}});
  const today = businessDate(), from = addDays(today, -20);

  await test('seed script completes on an empty database and refuses a second run', async () => {
    assert.equal(run.status, 0, run.stderr || run.stdout);
    assert.match(run.stdout, /Учебное наполнение завершено/);
    const again = spawnSync(process.execPath, ['scripts/seed-demo.mjs'], {encoding: 'utf8', env: {...process.env, FAIZA_API: api.base, FAIZA_ADMIN_LOGIN: api.adminLogin, FAIZA_ADMIN_PASSWORD: api.adminPassword}});
    assert.equal(again.status, 0); assert.match(again.stdout, /Наполнение пропущено/);
  });

  await test('three branches (Душанбе, Худжанд, Бохтар) each have a cashbox with an initial balance and an open shift today', async () => {
    const boxes = await api.expect('/cashboxes');
    assert.deepEqual([...new Set(boxes.map(x => x.city))].sort(), ['Бохтар', 'Душанбе', 'Худжанд']);
    for (const box of boxes) {
      assert.ok(box.openingAmount, box.code);
      assert.ok((await api.expect(`/shifts?cashboxId=${box.id}&date=${today}`)).some(x => x.status === 'open'), box.code + ' open shift');
      const oracle = api.scalar(`SELECT (COALESCE((SELECT "Amount" FROM "Openings" WHERE "CashboxId"=${box.id}),0)+COALESCE((SELECT SUM(CASE WHEN "Type"='income' THEN "Amount" ELSE -"Amount" END) FROM "Operations" WHERE "CashboxId"=${box.id} AND "Status"='posted'),0))::numeric(18,2)`);
      assert.equal((await api.expect(`/cashboxes/${box.id}/balance`)).balance, oracle, box.code + ' balance');
    }
  });

  await test('demo contains a transfer, partial payment, overdue debt, representative and drafts', async () => {
    assert.equal(api.scalar('SELECT count(*) FROM "Transfers"'), '1');
    const debts = await api.expect('/settlements');
    assert.ok(debts.some(x => x.status === 'partially_paid' && x.paidAmount === '2500.00'));
    assert.ok(debts.some(x => x.overdue));
    assert.ok(debts.some(x => x.representative === 'Рахимов Далер (демо)'));
    assert.equal(api.scalar(`SELECT count(*) FROM "Operations" WHERE "Status"='draft'`), '3');
    assert.ok(Number(api.scalar('SELECT count(*) FROM "RepresentativeReceipts"')) >= 2);
  });

  await test('one closed shift with a shortage feeds the Dashboard "attention" block', async () => {
    const dashboard = await api.expect('/dashboard?' + new URLSearchParams({from, to: today}));
    const item = dashboard.attention.find(x => x.code === 'shift_discrepancy');
    assert.ok(item, JSON.stringify(dashboard.attention)); assert.equal(item.amount, '-15.00');
    assert.ok(dashboard.attention.some(x => x.code === 'overdue_debt'));
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
