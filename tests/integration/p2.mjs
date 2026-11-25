// P2 regression suite (V7.1): D-11 search validation, OpenAPI contract with x-faiza-access,
// empirical role × route × scope matrix, D-09 encrypted backups restored into a disposable PostgreSQL.
// Requires Docker (postgres:18.3-alpine on 127.0.0.1:55433, tmpfs). Run from faiza-cash: node tests/integration/p2.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {startApi, suiteResults, businessDate, addDays, postOperation, psqlPath} from './harness.mjs';

const {test, finish} = suiteResults('p2');
const container = 'faiza-p2-restore-' + crypto.randomUUID().slice(0, 8);
const restorePassword = crypto.randomBytes(18).toString('hex');
const restorePort = '55433';
const encryptionKey = crypto.randomBytes(32).toString('base64');
const roles = ['admin', 'accountant', 'cashier', 'manager', 'auditor'];
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

  api = await startApi({port: 5089, name: 'p2', env: {
    ConnectionStrings__BackupRestore: `Host=127.0.0.1;Port=${restorePort};Database=faiza_restore;Username=faiza_restore;Password=${restorePassword}`,
    Backups__RestoreHost: '127.0.0.1', Backups__RestoreDatabase: 'faiza_restore', Backups__EncryptionKey: encryptionKey
  }});
  const today = businessDate(), from = addDays(today, -7);
  const income = await api.category('income');
  const box = (await api.cashbox('P2')).id, foreign = (await api.cashbox('P2F', 2)).id;
  await api.expect(`/cashboxes/${box}/opening`, {method: 'POST', body: {date: today, amount: '300.00'}}, 201);
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: box, date: today, openingBalance: '300.00'}}, 201);
  await postOperation(api, {cashboxId: box, date: today, type: 'income', amount: '25.00', categoryId: income});
  const users = {admin: {token: api.adminToken}};
  for (const role of roles.filter(x => x !== 'admin')) {
    users[role] = await api.user(role);
    await api.expect('/access', {method: 'POST', body: {userId: users[role].id, cashboxId: box}}, 204);
  }

  await test('D-11 /api/users rejects an oversized or NUL search string with 400', async () => {
    assert.equal((await api.request('/users?q=' + 'a'.repeat(161))).status, 400);
    assert.equal((await api.request('/users?q=' + encodeURIComponent('ad\0min'))).status, 400);
    const ok = await api.request('/users?q=' + encodeURIComponent(api.adminLogin));
    assert.equal(ok.status, 200);
  });

  await test('Audit filter accepts bare business dates (no 500) and a bare "to" date covers the whole day', async () => {
    const events = await api.expect('/audit?' + new URLSearchParams({from: today, to: today, action: 'cashbox_created', pageSize: '10'}));
    assert.ok(events.total >= 2, JSON.stringify(events).slice(0, 200));
    const offset = await api.expect('/audit?' + new URLSearchParams({from: today + 'T00:00:00+05:00', to: today + 'T23:59:59.9999999+05:00', action: 'cashbox_created', pageSize: '10'}));
    assert.equal(offset.total, events.total);
    assert.equal((await api.expect('/audit?' + new URLSearchParams({from: addDays(today, 1), action: 'cashbox_created'}))).total, 0);
  });

  let document;
  await test('OpenAPI document is admin-only and every operation declares x-faiza-access', async () => {
    assert.equal((await api.request('/openapi/v1.json', {token: null})).status, 401);
    assert.equal((await api.request('/openapi/v1.json', {token: users.cashier.token})).status, 403);
    document = await api.expect('/openapi/v1.json');
    assert.equal(document.info.title, 'FAIZA GOLD API');
    const operations = Object.entries(document.paths).flatMap(([route, item]) => Object.entries(item).filter(([method]) => ['get', 'post', 'put', 'patch', 'delete'].includes(method)).map(([method, op]) => ({route, method, op})));
    assert.ok(operations.length >= 80, 'operations: ' + operations.length);
    for (const {route, method, op} of operations) assert.ok(op['x-faiza-access'], `${method} ${route} lacks x-faiza-access`);
    const anonymous = operations.filter(x => x.op['x-faiza-access'].anonymous).map(x => `${x.method.toUpperCase()} ${x.route}`).sort();
    assert.deepEqual(anonymous, ['GET /health', 'POST /api/auth/login', 'POST /api/auth/refresh']);
  });

  const matrix = [];
  await test('Role × route × scope matrix: declared roles match real HTTP responses for every GET route', async () => {
    const fill = {cashboxId: box, branchId: 1, from, to: today, date: today, format: 'json'};
    const mismatches = [];
    for (const [route, item] of Object.entries(document.paths)) {
      const op = item.get;
      if (!op || !route.startsWith('/api/') || route.includes('{')) continue;
      const access = op['x-faiza-access'];
      const value = name => fill[Object.keys(fill).find(key => key.toLowerCase() === name.toLowerCase())];
      const required = (op.parameters || []).filter(p => p.in === 'query' && p.required).map(p => p.name);
      if (required.some(name => value(name) === undefined)) { matrix.push({route, ...access, skipped: 'required ' + required.join(',')}); continue; }
      const query = new URLSearchParams(Object.fromEntries((op.parameters || []).filter(p => p.in === 'query' && value(p.name) !== undefined).map(p => [p.name.charAt(0).toLowerCase() + p.name.slice(1), String(value(p.name))])));
      const url = route.slice(4) + (String(query) ? '?' + query : '');
      const row = {route, scope: access.scope, roles: access.roles, observed: {}};
      for (const role of roles) {
        const status = (await api.request(url, {token: users[role].token})).status;
        row.observed[role] = status;
        const allowed = access.roles === null || access.roles.includes(role);
        if (allowed ? [401, 403].includes(status) : status !== 403) mismatches.push(`${role} GET ${url}: ${status}, declared ${JSON.stringify(access.roles)}`);
        if (status >= 500) mismatches.push(`${role} GET ${url}: server error ${status}`);
      }
      if (access.scope === 'cashbox' && 'cashboxId' in Object.fromEntries(query)) {
        const foreignUrl = url.replace(`cashboxId=${box}`, `cashboxId=${foreign}`);
        const status = (await api.request(foreignUrl, {token: users.cashier.token})).status;
        row.foreignCashbox = status;
        if (status === 200) {
          const body = await (await api.request(foreignUrl, {token: users.cashier.token})).text();
          if (body.includes(`"cashboxId":${foreign}`) || body.includes(`"id":${foreign},`)) mismatches.push(`cashier sees foreign cashbox via ${foreignUrl}`);
        }
      }
      if (access.scope === 'branch' && query.has('branchId')) {
        // The cashier only has a cashbox in branch 1; branch 2 must be refused or contain nothing of the foreign cashbox.
        const foreignUrl = url.replace('branchId=1', 'branchId=2');
        const response = await api.request(foreignUrl, {token: users.cashier.token});
        row.foreignBranch = response.status;
        const body = Buffer.from(await response.arrayBuffer()).toString('latin1');
        if (response.status === 200 && (body.includes(`P2F-${api.suffix}`) || body.includes(`"id":${foreign},`))) mismatches.push(`cashier sees foreign branch via ${foreignUrl}`);
      }
      matrix.push(row);
    }
    fs.writeFileSync('.runtime/role-matrix.json', JSON.stringify({time: new Date().toISOString(), roles, rows: matrix}, null, 2));
    assert.deepEqual(mismatches, []);
    assert.ok(matrix.filter(x => !x.skipped).length >= 25, 'checked routes: ' + matrix.filter(x => !x.skipped).length);
  });

  await test('NFR-01/03 Dashboard under 20 concurrent readers: p95 < 500 ms with the shipped logging configuration', async () => {
    const url = `${api.base}/api/dashboard?from=${from}&to=${today}`;
    const latencies = [];
    let next = 0, failures = 0;
    const worker = async () => {
      while (next < 400) {
        next++;
        const begin = performance.now();
        const response = await fetch(url, {headers: {Authorization: 'Bearer ' + api.adminToken}});
        await response.arrayBuffer();
        latencies.push(performance.now() - begin);
        if (!response.ok) failures++;
      }
    };
    await Promise.all(Array.from({length: 20}, worker));
    latencies.sort((a, b) => a - b);
    const p95 = latencies[Math.ceil(latencies.length * 0.95) - 1];
    console.log(`  dashboard p50=${latencies[Math.floor(latencies.length / 2)].toFixed(0)} ms p95=${p95.toFixed(0)} ms`);
    assert.equal(failures, 0); assert.ok(p95 < 500, `p95 ${p95.toFixed(0)} ms`);
  });

  let backup;
  const dir = path.resolve('.runtime/p2-backups');
  await test('D-09 backups are AES-256-GCM encrypted at rest; verify and restore-test decrypt them in memory', async () => {
    const before = new Set(fs.existsSync(dir) ? fs.readdirSync(dir) : []);
    backup = await api.expect('/backups', {method: 'POST'});
    const created = fs.readdirSync(dir).filter(x => !before.has(x) && x !== 'status.json').sort();
    assert.deepEqual(created, [backup.id + '.dump', backup.id + '.dump.json'], 'no plain temporary files');
    assert.equal(backup.encrypted, true);
    const file = path.join(dir, backup.id + '.dump');
    const head = fs.readFileSync(file).subarray(0, 5);
    assert.equal(head.subarray(0, 4).toString(), 'FGBK'); assert.notEqual(head.toString(), 'PGDMP');
    assert.ok(!fs.readFileSync(file).includes(Buffer.from('P2 ' + api.suffix)), 'cashbox name readable in the backup file');
    const manifest = JSON.parse(fs.readFileSync(file + '.json', 'utf8'));
    assert.equal(manifest.FormatVersion, 3); assert.equal(manifest.Encrypted, true); assert.match(manifest.KeyId, /^[0-9a-f]{16}$/);
    assert.ok(!JSON.stringify(manifest).includes(encryptionKey));
    assert.equal((await api.expect(`/backups/${backup.id}/verify`, {method: 'POST'})).status, 'verified');
    const restored = await api.expect(`/backups/${backup.id}/restore-test`, {method: 'POST'});
    assert.equal(restored.status, 'restored'); assert.equal(restored.reconciliation.matched, true);
    assert.equal(restored.reconciliation.cashboxBalances[String(box)], '325.00');
    assert.equal(restoreScalar(`SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'`).out, '0');
  });

  await test('D-09 tampered ciphertext and a foreign key id are rejected with 422 and audited; the target stays empty', async () => {
    const source = path.join(dir, backup.id + '.dump');
    const clone = (id, mutateBytes, mutateManifest) => {
      const target = path.join(dir, id + '.dump');
      const bytes = fs.readFileSync(source); mutateBytes(bytes); fs.writeFileSync(target, bytes);
      const manifest = JSON.parse(fs.readFileSync(source + '.json', 'utf8'));
      Object.assign(manifest, {Id: id, Sha256: crypto.createHash('sha256').update(bytes).digest('hex'), SizeBytes: bytes.length}); mutateManifest(manifest);
      fs.writeFileSync(target + '.json', JSON.stringify(manifest));
    };
    const tampered = 'cash-p2-tampered-' + api.suffix.toLowerCase();
    clone(tampered, bytes => { bytes[Math.floor(bytes.length / 2)] ^= 0x01; }, () => {});
    let response = await api.request(`/backups/${tampered}/restore-test`, {method: 'POST'});
    assert.equal(response.status, 422); assert.match((await response.json()).detail, /расшифровать/);
    response = await api.request(`/backups/${tampered}/verify`, {method: 'POST'});
    assert.equal(response.status, 422);
    const otherKey = 'cash-p2-otherkey-' + api.suffix.toLowerCase();
    clone(otherKey, () => {}, manifest => { manifest.KeyId = '0000000000000000'; });
    response = await api.request(`/backups/${otherKey}/restore-test`, {method: 'POST'});
    assert.equal(response.status, 422); assert.match((await response.json()).detail, /другим ключом/);
    const audit = await api.expect('/audit?action=backup_restore_failed&pageSize=20');
    for (const id of [tampered, otherKey]) assert.ok(audit.items.some(x => JSON.parse(x.details).id === id), id);
    assert.equal(restoreScalar(`SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'`).out, '0');
    assert.equal((await api.expect(`/backups/${backup.id}/restore-test`, {method: 'POST'})).status, 'restored');
  });
} catch (error) {
  console.error(error.message); process.exitCode = 1;
} finally {
  api?.stop();
  docker(['rm', '-f', container]);
  finish();
}
