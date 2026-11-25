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
  api = await startApi({port: 5090, name: 'wave1', env: {Attachments__Directory: '.runtime/wave1-attachments'}});
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

  // ---------- 2. Вложения ----------
  await test('ATTACH-01 cashier attaches a scan to own draft; content keeps type and checksum', async () => {
    const row = await draft(cashier.token, 'expense', '10.00');
    const response = await upload('/attachments', {token: cashier.token, fields: {ownerType: 'operation', ownerId: row.id}, file: PNG, name: 'C:\\fakepath\\чек.png'});
    assert.equal(response.status, 201, await response.clone().text());
    const created = await response.json();
    assert.equal(created.contentType, 'image/png'); assert.equal(created.fileName, 'чек.png'); assert.equal(created.sizeBytes, PNG.length);
    assert.equal(created.sha256, crypto.createHash('sha256').update(PNG).digest('hex')); assert.equal(created.uploadedBy, cashier.id);
    const list = await api.expect(`/attachments?ownerType=operation&ownerId=${row.id}`, {token: auditor.token});
    assert.deepEqual(list.map(x => x.id), [created.id]);
    const content = await api.request(`/attachments/${created.id}/content`, {token: auditor.token});
    assert.equal(content.status, 200); assert.equal(content.headers.get('content-type'), 'image/png');
    assert.match(content.headers.get('content-disposition'), /attachment/);
    assert.ok(Buffer.from(await content.arrayBuffer()).equals(PNG));
    await api.expect(`/attachments/${created.id}`, {method: 'DELETE', token: cashier.token}, 204);
    assert.deepEqual(await api.expect(`/attachments?ownerType=operation&ownerId=${row.id}`, {token: cashier.token}), []);
  });

  await test('ATTACH-02 signature, size, role and cashbox checks', async () => {
    const row = await draft(cashier.token, 'expense', '11.00');
    const fields = {ownerType: 'operation', ownerId: row.id};
    assert.equal((await upload('/attachments', {token: cashier.token, fields, file: EXE, name: 'scan.jpg', type: 'image/jpeg'})).status, 400, 'exe renamed to .jpg');
    const big = Buffer.concat([PDF, Buffer.alloc(5 * 1024 * 1024, 0x20)]);
    assert.equal((await upload('/attachments', {token: cashier.token, fields, file: big, name: 'big.pdf', type: 'application/pdf'})).status, 400, 'over 5 MB');
    assert.equal((await upload('/attachments', {token: auditor.token, fields, file: PDF, name: 'a.pdf'})).status, 403, 'auditor');
    assert.equal((await upload('/attachments', {token: pureAdmin.token, fields, file: PDF, name: 'a.pdf'})).status, 403, 'admin');
    const foreign = await api.expect('/operations', {method: 'POST', body: {cashboxId: foreignBox.id, date: today, type: 'income', amount: '1.00', description: 'Foreign', categoryId: income, counterpartyId: null, documentTypeId: null, requestKey: crypto.randomUUID()}}, 201);
    assert.equal((await upload('/attachments', {token: cashier.token, fields: {ownerType: 'operation', ownerId: foreign.id}, file: PDF, name: 'a.pdf'})).status, 403, 'foreign cashbox');
    assert.equal((await api.request(`/attachments?ownerType=operation&ownerId=${foreign.id}`, {token: cashier.token})).status, 403);
    const other = await draft(accountant.token, 'expense', '12.00');
    assert.equal((await upload('/attachments', {token: cashier.token, fields: {ownerType: 'operation', ownerId: other.id}, file: PDF, name: 'a.pdf'})).status, 403, 'cashier on another author draft');
    assert.equal((await upload('/attachments', {token: accountant.token, fields: {ownerType: 'operation', ownerId: other.id}, file: PDF, name: 'a.pdf'})).status, 201, 'accountant on any draft');
  });

  await test('ATTACH-03 posted document locks attachments in code and in the database trigger', async () => {
    const row = await draft(cashier.token, 'expense', '13.00');
    const created = await (await upload('/attachments', {token: cashier.token, fields: {ownerType: 'operation', ownerId: row.id}, file: PDF, name: 'act.pdf', type: 'application/pdf'})).json();
    await post(cashier.token, row);
    await api.expect(`/attachments/${created.id}`, {method: 'DELETE', token: cashier.token}, 409);
    assert.equal((await upload('/attachments', {token: cashier.token, fields: {ownerType: 'operation', ownerId: row.id}, file: PNG})).status, 409);
    const direct = api.sql(`DELETE FROM "Attachments" WHERE "Id"='${created.id}'`, false);
    assert.notEqual(direct.status, 0); assert.match(direct.stderr, /cannot be deleted/);
    assert.equal(api.scalar(`SELECT count(*) FROM "Attachments" WHERE "Id"='${created.id}'`), '1');
  });

  // ---------- 3. Авансы ----------
  const employee = await api.counterparty('Подотчётный сотрудник ' + api.suffix, 'employee');
  const client = await api.counterparty('Клиент ' + api.suffix, 'client');
  const postedFor = async (type, amount, counterpartyId) => post(accountant.token, await draft(accountant.token, type, amount, {counterpartyId}));
  await test('ADV-01 full cycle: issue → report → return of the remainder', async () => {
    const issue = await postedFor('expense', '1000.00', employee);
    await api.expect('/advances', {method: 'POST', token: cashier.token, body: {operationId: issue.id, purpose: 'Командировка', dueDate: addDays(today, 5)}}, 403);
    await api.expect('/advances', {method: 'POST', token: pureAdmin.token, body: {operationId: issue.id, purpose: 'Командировка', dueDate: addDays(today, 5)}}, 403);
    const toClient = await postedFor('expense', '50.00', client);
    await api.expect('/advances', {method: 'POST', token: accountant.token, body: {operationId: toClient.id, purpose: 'Не сотрудник', dueDate: today}}, 409);
    const unposted = await draft(accountant.token, 'expense', '50.00', {counterpartyId: employee});
    await api.expect('/advances', {method: 'POST', token: accountant.token, body: {operationId: unposted.id, purpose: 'Черновик', dueDate: today}}, 409);
    const advance = await api.expect('/advances', {method: 'POST', token: accountant.token, body: {operationId: issue.id, purpose: 'Командировка в Худжанд', dueDate: addDays(today, 5)}}, 201);
    assert.equal(advance.status, 'issued'); assert.equal(advance.amount, '1000.00'); assert.equal(advance.overdue, false);
    await api.expect('/advances', {method: 'POST', token: accountant.token, body: {operationId: issue.id, purpose: 'Повтор', dueDate: today}}, 409);
    await api.expect(`/advances/${advance.id}/report`, {method: 'POST', token: accountant.token, body: {lines: []}}, 400);
    await api.expect(`/advances/${advance.id}/report`, {method: 'POST', token: accountant.token, body: {lines: [{date: today, description: 'Билет', amount: '1.00', categoryId: income}]}}, 400);
    const reported = await api.expect(`/advances/${advance.id}/report`, {method: 'POST', token: accountant.token, body: {lines: [{date: today, description: 'Билет', amount: '450.00', categoryId: expense}, {date: today, description: 'Гостиница', amount: '250.00', categoryId: expense}]}}, 200);
    assert.equal(reported.status, 'reported'); assert.equal(reported.spent, '700.00'); assert.equal(reported.remainder, '300.00'); assert.equal(reported.lines.length, 2);
    await api.expect(`/advances/${advance.id}/settle`, {method: 'POST', token: accountant.token, body: {}}, 409);
    const wrong = await postedFor('income', '299.00', employee);
    await api.expect(`/advances/${advance.id}/settle`, {method: 'POST', token: accountant.token, body: {operationId: wrong.id}}, 409);
    const refund = await postedFor('income', '300.00', employee);
    const closed = await api.expect(`/advances/${advance.id}/settle`, {method: 'POST', token: accountant.token, body: {operationId: refund.id}}, 200);
    assert.equal(closed.status, 'closed'); assert.equal(closed.settlementOperationId, refund.id);
    const card = await api.expect(`/advances/${advance.id}`, {token: auditor.token});
    assert.equal(card.lines.length, 2); assert.deepEqual(card.attachments, []);
    const audit = await api.expect('/audit?entity=advance&pageSize=100');
    for (const action of ['advance_issued', 'advance_reported', 'advance_closed']) assert.ok(audit.items.some(x => x.action === action && x.entityId === advance.id), action);
  });

  await test('ADV-02 overspending is reimbursed by an expense order; reports export', async () => {
    const issue = await postedFor('expense', '500.00', employee);
    const advance = await api.expect('/advances', {method: 'POST', token: accountant.token, body: {operationId: issue.id, purpose: 'Хозяйственные нужды', dueDate: today}}, 201);
    const scan = await upload('/attachments', {token: accountant.token, fields: {ownerType: 'advance', ownerId: advance.id}, file: PNG, name: 'чек.png'});
    assert.equal(scan.status, 201);
    const reported = await api.expect(`/advances/${advance.id}/report`, {method: 'POST', token: accountant.token, body: {lines: [{date: today, description: 'Канцтовары', amount: '650.00', categoryId: expense}]}}, 200);
    assert.equal(reported.remainder, '-150.00'); assert.equal(reported.attachments.length, 1);
    const refundToClient = await postedFor('income', '150.00', employee);
    await api.expect(`/advances/${advance.id}/settle`, {method: 'POST', token: accountant.token, body: {operationId: refundToClient.id}}, 409);
    const reimbursement = await postedFor('expense', '150.00', employee);
    assert.equal((await api.expect(`/advances/${advance.id}/settle`, {method: 'POST', token: accountant.token, body: {operationId: reimbursement.id}}, 200)).status, 'closed');
    const scanId = (await scan.json()).id;
    await api.expect(`/attachments/${scanId}`, {method: 'DELETE', token: accountant.token}, 409);
    const list = await api.expect('/advances?status=closed', {token: cashier.token});
    assert.ok(list.items.some(x => x.id === advance.id));
    const report = await api.expect(`/reports/advances?cashboxId=${box.id}`, {token: manager.token});
    assert.ok(report.items.length >= 2);
    const csv = await api.request('/reports/advances.csv', {token: accountant.token});
    assert.equal(csv.status, 200); assert.match(await csv.text(), /Подотчётный сотрудник/);
    const pdf = await api.request('/reports/advances.pdf', {token: auditor.token});
    assert.equal(pdf.status, 200); assert.equal(pdf.headers.get('content-type'), 'application/pdf');
  });
} catch (error) {
  console.error(error.stack || error.message); process.exitCode = 1;
} finally {
  api?.stop();
  finish();
}
