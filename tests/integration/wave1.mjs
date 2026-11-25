// Набор волны 1: согласование расходов, вложения, авансы, пересчёт по купюрам и инвентаризация,
// расширенный dashboard, панель администратора, логотип и аватары.
// Запуск из faiza-cash: node tests/integration/wave1.mjs (только на БД с суффиксом _test).
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {startApi, suiteResults, businessDate, addDays} from './harness.mjs';

const {test, finish} = suiteResults('wave1');
let api;
// Минимальные корректные файлы для проверки сигнатур (magic bytes).
const PNG = Buffer.from('89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d4944415478da63f8ffff3f0005fe02fea7d6a5b70000000049454e44ae426082', 'hex');
const PDF = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n', 'latin1');
const EXE = Buffer.concat([Buffer.from('MZ'), Buffer.alloc(200, 0x90)]);

async function upload(path, {token, fields = {}, file, name = 'scan.png', type = 'image/png', method = 'POST'}) {
  const form = new FormData();
  for (const [key, value] of Object.entries(fields)) form.append(key, String(value));
  if (file) form.append('file', new Blob([file], {type}), name);
  return fetch(api.base + '/api' + path, {method, headers: {Authorization: 'Bearer ' + (token ?? api.adminToken), 'X-Faiza-Request': '1'}, body: form});
}

try {
  api = await startApi({port: 5090, name: 'wave1'});
  const today = businessDate();
  const income = await api.category('income'), expense = await api.category('expense');
  const box = await api.cashbox('W1A'), foreignBox = await api.cashbox('W1F');
  await api.expect(`/cashboxes/${box.id}/opening`, {method: 'POST', body: {date: addDays(today, -3), amount: '20000.00'}}, 201);
  await api.expect(`/cashboxes/${foreignBox.id}/opening`, {method: 'POST', body: {date: addDays(today, -3), amount: '100.00'}}, 201);
  const cashier = await api.user('cashier'), manager = await api.user('manager'), accountant = await api.user('accountant'), auditor = await api.user('auditor');
  const pureAdmin = await api.user('admin');
  for (const user of [cashier, manager, accountant, auditor]) await api.expect('/access', {method: 'POST', body: {userId: user.id, cashboxId: box.id}}, 204);
  await api.expect('/shifts/open', {method: 'POST', token: cashier.token, body: {cashboxId: box.id, date: today, openingBalance: '20000.00'}}, 201);
  await api.expect('/shifts/open', {method: 'POST', body: {cashboxId: foreignBox.id, date: today, openingBalance: '100.00'}}, 201);
  const draft = (token, type, amount, extra = {}) => api.expect('/operations', {method: 'POST', token, body: {cashboxId: box.id, date: today, type, amount, description: `Wave1 ${type} ${amount}`, categoryId: type === 'income' ? income : expense, counterpartyId: null, documentTypeId: null, requestKey: crypto.randomUUID(), ...extra}}, 201);
  const post = (token, row, status = 200) => api.expect(`/operations/${row.id}/post`, {method: 'POST', token, body: {expectedVersion: row.version}}, status);

  // ---------- 1. Согласование расходов ----------
  await test('APPROVAL-01 threshold is stored in settings and validated', async () => {
    const settings = await api.expect('/settings');
    assert.equal(settings.expenseApprovalThreshold, null);
    await api.expect('/settings', {method: 'PUT', body: {name: settings.name, currency: settings.currency, expenseApprovalThreshold: '0.00'}}, 400);
    await api.expect('/settings', {method: 'PUT', body: {name: settings.name, currency: settings.currency, expenseApprovalThreshold: '5000.00'}}, 204);
    assert.equal((await api.expect('/settings')).expenseApprovalThreshold, '5000.00');
  });

  await test('APPROVAL-02 expense above threshold needs manager approval; edits reset the status', async () => {
    const row = await draft(cashier.token, 'expense', '6000.00');
    assert.equal(row.approvalStatus, 'none');
    const blocked = await api.request(`/operations/${row.id}/post`, {method: 'POST', token: cashier.token, body: {expectedVersion: row.version}});
    assert.equal(blocked.status, 409); assert.match(await blocked.text(), /Расход требует согласования руководителя/);
    await api.expect(`/operations/${row.id}/approve`, {method: 'POST', token: manager.token, body: {}}, 409); // ещё не отправлен
    const pending = await api.expect(`/operations/${row.id}/submit-approval`, {method: 'POST', token: cashier.token}, 200);
    assert.equal(pending.approvalStatus, 'pending');
    await api.expect(`/operations/${row.id}/submit-approval`, {method: 'POST', token: cashier.token}, 409);
    const list = await api.expect(`/approvals?status=pending&cashboxId=${box.id}`, {token: manager.token});
    assert.ok(list.items.some(x => x.id === row.id));
    await api.expect(`/operations/${row.id}/approve`, {method: 'POST', token: pureAdmin.token, body: {}}, 403);
    await api.expect(`/operations/${row.id}/approve`, {method: 'POST', token: cashier.token, body: {}}, 403);
    const approved = await api.expect(`/operations/${row.id}/approve`, {method: 'POST', token: manager.token, body: {comment: 'Согласовано'}}, 200);
    assert.equal(approved.approvalStatus, 'approved'); assert.equal(approved.approvedBy, manager.id); assert.equal(approved.approvalComment, 'Согласовано');
    const edited = await api.expect(`/operations/${row.id}`, {method: 'PATCH', token: cashier.token, body: {description: 'Изменённое основание', version: approved.version}}, 200);
    assert.equal(edited.approvalStatus, 'none'); assert.equal(edited.approvedBy, null);
    await post(cashier.token, edited, 409);
    await api.expect(`/operations/${row.id}/submit-approval`, {method: 'POST', token: cashier.token}, 200);
    const again = await api.expect(`/operations/${row.id}/approve`, {method: 'POST', token: manager.token, body: {}}, 200);
    const posted = await post(cashier.token, again);
    assert.equal(posted.status, 'posted'); assert.equal(posted.approvalStatus, 'approved');
    const audit = await api.expect(`/audit?entity=operation&pageSize=100`);
    for (const action of ['approval_submitted', 'approval_approved', 'approval_reset']) assert.ok(audit.items.some(x => x.action === action && x.entityId === row.id), action);
  });

  await test('APPROVAL-03 author cannot approve own expense; reject requires a reason; below threshold posts directly', async () => {
    // Пользователь с ролями бухгалтера и руководителя создаёт расход и пытается согласовать его сам.
    const password = crypto.randomBytes(14).toString('hex') + 'Aa1';
    const dual = await api.expect('/users', {method: 'POST', body: {login: 'w1-dual-' + api.suffix.toLowerCase(), fullName: 'Бухгалтер-руководитель', password, roles: ['accountant', 'manager'], mustChangePassword: false}}, 201);
    await api.expect('/access', {method: 'POST', body: {userId: dual.id, cashboxId: box.id}}, 204);
    const dualToken = (await api.expect('/auth/login', {method: 'POST', token: null, body: {login: dual.login, password}})).accessToken;
    const mine = await draft(dualToken, 'expense', '8000.00');
    await api.expect(`/operations/${mine.id}/submit-approval`, {method: 'POST', token: dualToken}, 200);
    const self = await api.request(`/operations/${mine.id}/approve`, {method: 'POST', token: dualToken, body: {}});
    assert.equal(self.status, 409); assert.match(await self.text(), /собственный документ/);
    const own = await draft(accountant.token, 'expense', '7000.00');
    await api.expect(`/operations/${own.id}/submit-approval`, {method: 'POST', token: accountant.token}, 200);
    await api.expect(`/operations/${own.id}/reject`, {method: 'POST', token: manager.token, body: {reason: '  '}}, 400);
    const rejected = await api.expect(`/operations/${own.id}/reject`, {method: 'POST', token: manager.token, body: {reason: 'Нет первичного документа'}}, 200);
    assert.equal(rejected.approvalStatus, 'rejected'); assert.equal(rejected.approvalComment, 'Нет первичного документа');
    assert.ok((await api.expect('/approvals?status=rejected', {token: accountant.token})).items.some(x => x.id === own.id));
    const small = await draft(cashier.token, 'expense', '4999.99');
    await post(cashier.token, small);
    await api.expect(`/operations/${small.id}/submit-approval`, {method: 'POST', token: cashier.token}, 409);
  });
} catch (error) {
  console.error(error.stack || error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
