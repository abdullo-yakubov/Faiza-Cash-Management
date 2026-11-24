// FG7-004 / D-08: repeatable isolated restore with reconciliation.
// Starts a disposable postgres:18.3-alpine container (tmpfs, loopback port 55432) as the ONLY restore target.
// Run from faiza-cash: node tests/integration/restore.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {startApi, suiteResults, businessDate, postOperation, psqlPath} from './harness.mjs';

const {test, finish} = suiteResults('restore');
const container = 'faiza-restore-test-' + crypto.randomUUID().slice(0, 8);
const restorePassword = crypto.randomBytes(18).toString('hex');
const restorePort = '55432';
const docker = args => spawnSync('docker', args, {encoding: 'utf8'});
const restoreScalar = statement => {
  const r = spawnSync(psqlPath, ['-h', '127.0.0.1', '-p', restorePort, '-U', 'faiza_restore', '-d', 'faiza_restore', '-w', '-At', '-v', 'ON_ERROR_STOP=1'],
    {env: {...process.env, PGPASSWORD: restorePassword}, input: statement, encoding: 'utf8'});
  return {ok: r.status === 0, out: r.stdout.trim(), err: r.stderr};
};
let api;
try {
  const started = docker(['run', '-d', '--rm', '--name', container, '-e', 'POSTGRES_DB=faiza_restore', '-e', 'POSTGRES_USER=faiza_restore', '-e', `POSTGRES_PASSWORD=${restorePassword}`,
    '-p', `127.0.0.1:${restorePort}:5432`, '--tmpfs', '/var/lib/postgresql', 'postgres:18.3-alpine']);
  assert.equal(started.status, 0, 'Docker could not start the disposable restore database: ' + started.stderr);
  let stable = 0;
  for (let i = 0; i < 160 && stable < 4; i++) { stable = restoreScalar('SELECT 1').ok ? stable + 1 : 0; await new Promise(r => setTimeout(r, 500)); }
  assert.equal(stable, 4, 'Restore database did not become ready');

  api = await startApi({port: 5086, name: 'restore', env: {
    ConnectionStrings__BackupRestore: `Host=127.0.0.1;Port=${restorePort};Database=faiza_restore;Username=faiza_restore;Password=${restorePassword}`,
    Backups__RestoreHost: '127.0.0.1', Backups__RestoreDatabase: 'faiza_restore'
  }});
  const today = businessDate();
  const income = await api.category('income'), expense = await api.category('expense');
  const party = await api.counterparty('Restore client ' + api.suffix);
  const box = (await api.cashbox('RST')).id;
  await api.expect(`/cashboxes/${box}/opening`, {method: 'POST', body: {date: today, amount: '700.00'}}, 201);
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: box, date: today, openingBalance: '700.00'}}, 201);
  await postOperation(api, {cashboxId: box, date: today, type: 'expense', amount: '45.50', categoryId: expense});
  const payment = await postOperation(api, {cashboxId: box, date: today, type: 'income', amount: '120.00', categoryId: income, counterpartyId: party});
  const settlement = await api.expect('/settlements', {method: 'POST', body: {counterpartyId: party, cashboxId: box, direction: 'receivable', basisType: 'order', basisReference: 'RST-' + api.suffix, totalAmount: '300.00', dueDate: today}}, 201);
  await api.expect(`/settlements/${settlement.id}/allocate`, {method: 'POST', body: {operationId: payment.id, amount: '120.00'}});
  const backup = await api.expect('/backups', {method: 'POST'});

  await test('Two restore-test runs in a row both succeed and reconcile balances, debts, documents and FKs', async () => {
    for (const attempt of [1, 2]) {
      const result = await api.expect(`/backups/${backup.id}/restore-test`, {method: 'POST'});
      assert.equal(result.status, 'restored', `attempt ${attempt}`);
      assert.equal(result.reconciliation.matched, true, JSON.stringify(result.reconciliation));
      assert.equal(result.reconciliation.postedOperations, 2);
      assert.equal(result.reconciliation.cashboxBalances[String(box)], '774.50');
      assert.equal(result.reconciliation.settlementTotal, '300.00');
      assert.equal(result.reconciliation.settlementPaid, '120.00');
      assert.equal(result.reconciliation.allocationTotal, '120.00');
      assert.ok(result.reconciliation.foreignKeys > 10);
      assert.ok(result.durationMs >= 0);
    }
    const audit = await api.expect('/audit?action=backup_restore_tested&pageSize=10');
    assert.equal(audit.total, 2);
  });

  await test('Restore database is emptied after each test restore', async () => {
    const tables = restoreScalar(`SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','S','f')`);
    assert.ok(tables.ok, tables.err); assert.equal(tables.out, '0');
  });

  const dir = path.resolve('.runtime/restore-backups');
  const source = path.join(dir, backup.id + '.dump');
  const cloneBackup = (id, mutate, recomputeManifest) => {
    const target = path.join(dir, id + '.dump');
    fs.copyFileSync(source, target);
    const bytes = fs.readFileSync(target); mutate(bytes); fs.writeFileSync(target, bytes);
    const manifest = JSON.parse(fs.readFileSync(source + '.json', 'utf8'));
    manifest.Id = id;
    if (recomputeManifest) { manifest.Sha256 = crypto.createHash('sha256').update(bytes).digest('hex'); manifest.SizeBytes = bytes.length; }
    fs.writeFileSync(target + '.json', JSON.stringify(manifest));
  };

  await test('Checksum mismatch is rejected with a clear error and an audit record', async () => {
    const id = 'cash-corrupt-checksum-' + api.suffix.toLowerCase();
    cloneBackup(id, bytes => { bytes[Math.floor(bytes.length / 2)] ^= 0xff; }, false);
    const response = await api.request(`/backups/${id}/restore-test`, {method: 'POST'});
    assert.equal(response.status, 422);
    assert.match((await response.json()).detail, /Контрольная сумма|манифест/i);
    const audit = await api.expect('/audit?action=backup_restore_failed&pageSize=10');
    assert.ok(audit.items.some(x => JSON.parse(x.details).id === id));
  });

  await test('Damaged dump with a forged manifest fails pg_restore cleanly, is audited and leaves the target empty', async () => {
    const id = 'cash-corrupt-content-' + api.suffix.toLowerCase();
    cloneBackup(id, bytes => { for (let i = 200; i < Math.min(bytes.length, 4000); i++) bytes[i] = 0x41; }, true);
    const response = await api.request(`/backups/${id}/restore-test`, {method: 'POST'});
    assert.equal(response.status, 422);
    assert.match((await response.json()).detail, /не восстанавливается|повреждён/i);
    const audit = await api.expect('/audit?action=backup_restore_failed&pageSize=10');
    assert.ok(audit.items.some(x => JSON.parse(x.details).id === id));
    assert.equal(restoreScalar(`SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'`).out, '0');
    const again = await api.expect(`/backups/${backup.id}/restore-test`, {method: 'POST'});
    assert.equal(again.status, 'restored');
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  docker(['rm', '-f', container]);
  finish();
}
