// Shared harness for PostgreSQL suites. Uses only the dedicated loopback *_test database
// from .runtime/local.json and resets its public schema before the API starts.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import {spawn, spawnSync} from 'node:child_process';

export const psqlPath = 'C:/Program Files/PostgreSQL/18/bin/psql.exe';
export const dll = 'backend/src/FaizaCash.Api/bin/Debug/net10.0/FaizaCash.Api.dll';
export const businessDate = (offsetDays = 0) => {
  const today = new Intl.DateTimeFormat('sv-SE', {timeZone: 'Asia/Dushanbe'}).format(new Date());
  return new Date(Date.parse(today + 'T00:00:00Z') + offsetDays * 86400000).toISOString().slice(0, 10);
};
export const addDays = (date, days) => new Date(Date.parse(date + 'T00:00:00Z') + days * 86400000).toISOString().slice(0, 10);

export function testDatabase() {
  const cfg = JSON.parse(fs.readFileSync('.runtime/local.json', 'utf8'));
  const fields = Object.fromEntries(cfg.ConnectionStrings__CashDb.split(';').map(part => {
    const at = part.indexOf('=');
    return [part.slice(0, at).toLowerCase(), part.slice(at + 1)];
  }));
  assert.ok(['localhost', '127.0.0.1', '::1'].includes(fields.host), 'Tests require loopback PostgreSQL');
  const database = fields.database + '_test';
  assert.match(database, /^faiza_[a-z0-9]+_test$/, 'Tests require the dedicated _test database');
  return {cfg, fields, database, connection: cfg.ConnectionStrings__CashDb.replace(/Database=([^;]+)/i, 'Database=' + database)};
}

export function makeSql({fields, database}) {
  const run = (statement, mustSucceed = true) => {
    const result = spawnSync(psqlPath, ['-h', fields.host, '-p', fields.port || '5432', '-U', fields.username, '-d', database, '-w', '-At', '-v', 'ON_ERROR_STOP=1'],
      {env: {...process.env, PGPASSWORD: fields.password}, input: statement, encoding: 'utf8'});
    if (mustSucceed) assert.equal(result.status, 0, result.stderr);
    return result;
  };
  return {sql: run, scalar: statement => run(statement).stdout.trim()};
}

async function portFree(port) {
  await new Promise((resolve, reject) => {
    const probe = net.createServer();
    probe.once('error', () => reject(new Error(`Port ${port} is busy; stop the process using it before running tests.`)));
    probe.listen(port, '127.0.0.1', () => probe.close(resolve));
  });
}

export function suiteResults(name) {
  const results = [];
  async function test(title, run) {
    try { await run(); results.push({name: title, status: 'passed'}); console.log('PASS ' + title); }
    catch (error) { results.push({name: title, status: 'failed', error: error.message}); console.log('FAIL ' + title + ': ' + error.message); }
  }
  function finish() {
    fs.writeFileSync(`.runtime/${name}-results.json`, JSON.stringify({time: new Date().toISOString(), database: 'dedicated loopback _test database', results}, null, 2));
    const failed = results.filter(x => x.status !== 'passed').length;
    console.log(`${name}: ${results.length - failed} passed, ${failed} failed, ${results.length} total.`);
    if (failed || results.length === 0) process.exitCode = 1;
  }
  return {test, finish, results};
}

export async function startApi({port, name, env = {}}) {
  assert.ok(fs.existsSync(dll), 'Build the backend first: dotnet build backend/FaizaCash.sln');
  const db = testDatabase();
  const {sql, scalar} = makeSql(db);
  await portFree(port);
  const lock = '.runtime/database-tests.lock';
  try { fs.closeSync(fs.openSync(lock, 'wx')); }
  catch { throw new Error('Another database test suite is active. Run suites sequentially.'); }
  const removeLock = () => { try { fs.unlinkSync(lock); } catch {} };
  process.once('exit', removeLock);
  sql('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
  const base = `http://127.0.0.1:${port}`;
  const adminLogin = name + '-admin';
  const initialPassword = crypto.randomBytes(18).toString('hex') + 'Aa1';
  const log = fs.openSync(`.runtime/${name}-server.log`, 'w');
  const server = spawn('dotnet', [dll], {
    // Content root = build output, so the shipped appsettings.json (logging levels etc.) is loaded as in the Docker image.
    env: {...process.env, ...db.cfg, ConnectionStrings__CashDb: db.connection, ASPNETCORE_URLS: base, ASPNETCORE_ENVIRONMENT: 'Test', ASPNETCORE_CONTENTROOT: path.resolve(path.dirname(dll)),
      Seed__AdminLogin: adminLogin, Seed__AdminPassword: initialPassword, Jwt__Key: crypto.randomBytes(48).toString('hex'),
      Jwt__Issuer: 'FaizaCash', Jwt__Audience: 'FaizaCash', Logs__Directory: `.runtime/${name}-logs`, Backups__Directory: `.runtime/${name}-backups`, Backups__IntervalHours: '0', Backups__PostgresBin: 'C:/Program Files/PostgreSQL/18/bin', ...env},
    stdio: ['ignore', log, log]
  });
  const stop = () => { if (server.exitCode === null) server.kill(); try { fs.closeSync(log); } catch {} removeLock(); };
  let ready = false;
  for (let i = 0; i < 120 && !ready; i++) {
    if (server.exitCode !== null) { stop(); throw new Error(`Test API exited during startup; inspect .runtime/${name}-server.log`); }
    try { ready = (await fetch(base + '/health', {signal: AbortSignal.timeout(1000)})).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (!ready) { stop(); throw new Error('Test API did not become ready'); }
  let adminToken;
  async function request(path, {method = 'GET', body, token = adminToken, raw} = {}) {
    return fetch(base + '/api' + path, {
      method, signal: AbortSignal.timeout(120000),
      headers: {'Content-Type': 'application/json', 'X-Faiza-Request': '1', ...(token ? {Authorization: 'Bearer ' + token} : {})},
      body: raw !== undefined ? raw : body === undefined ? undefined : JSON.stringify(body)
    });
  }
  async function expect(path, options = {}, status = 200) {
    const response = await request(path, options);
    const text = await response.text();
    assert.equal(response.status, status, `${options.method || 'GET'} ${path}: expected ${status}, received ${response.status} ${text.slice(0, 400)}`);
    if (!text) return null;
    try { return JSON.parse(text); } catch { return text; }
  }
  const login = await expect('/auth/login', {method: 'POST', body: {login: adminLogin, password: initialPassword}, token: null});
  adminToken = login.accessToken;
  const adminPassword = crypto.randomBytes(18).toString('hex') + 'Aa1';
  await expect('/auth/change-password', {method: 'POST', body: {oldPassword: initialPassword, newPassword: adminPassword}}, 204);
  const currencies = await expect('/currencies');
  const suffix = crypto.randomUUID().slice(0, 6).toUpperCase();

  async function user(role) {
    const password = crypto.randomBytes(14).toString('hex') + 'Aa1';
    const created = await expect('/users', {method: 'POST', body: {login: `${name}-${role}-${suffix}-${crypto.randomUUID().slice(0, 4)}`.toLowerCase(), fullName: `Test ${role}`, password, roles: [role], mustChangePassword: false}}, 201);
    const token = (await expect('/auth/login', {method: 'POST', body: {login: created.login, password}, token: null})).accessToken;
    return {...created, token};
  }
  async function cashbox(code, branchId = 1, extra = {}) {
    return expect('/cashboxes', {method: 'POST', body: {code: `${code}-${suffix}`, name: `${code} ${suffix}`, currencyId: currencies[0].id, balanceLimit: null, branchId, ...extra}}, 201);
  }
  async function category(type) {
    const title = `${name} ${type} ${suffix}`;
    await expect('/categories', {method: 'POST', body: {name: title, type, parentId: null}}, 204);
    return (await expect('/categories?type=' + type)).find(x => x.name === title).id;
  }
  async function counterparty(title, kind = 'client') {
    await expect('/counterparties', {method: 'POST', body: {name: title, kind, taxCode: null, contact: null}}, 204);
    return (await expect('/counterparties?q=' + encodeURIComponent(title))).find(x => x.name === title).id;
  }
  return {base, request, expect, sql, scalar, stop, currencies, suffix, user, cashbox, category, counterparty, adminLogin, adminPassword, get adminToken() { return adminToken; }};
}

export async function postOperation(api, {cashboxId, date, type, amount, categoryId, counterpartyId = null, description, token}) {
  const draft = await api.expect('/operations', {method: 'POST', token, body: {cashboxId, date, type, amount, description: description || `Test ${type}`, categoryId, counterpartyId, documentTypeId: null, requestKey: crypto.randomUUID()}}, 201);
  return api.expect(`/operations/${draft.id}/post`, {method: 'POST', token, body: {expectedVersion: draft.version}});
}

export function csvRows(text) {
  return text.replace(/^\uFEFF/, '').trim().split(/\r?\n/).map(line => line.split(';').map(cell => cell.replace(/^"|"$/g, '').replace(/""/g, '"')));
}
