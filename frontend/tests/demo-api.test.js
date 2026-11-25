// Моки демо-режима отдают ровно те формы JSON, что описаны в API-контракте волны 1.
import test from 'node:test';
import assert from 'node:assert/strict';
import {demoResponse, demoAugment, isWave1Route} from '../src/services/demo.js';
globalThis.__FAIZA_DEMO_NO_DELAY = true;
globalThis.__FAIZA_DEMO = true;
const money = /^-?\d+\.\d{2}$/;

test('dashboard дополняется полями п.7, существующие поля не меняются',()=>{
  const out=demoAugment('GET','/dashboard?from=2026-11-01',{totalBalance:'10.00'});
  assert.equal(out.totalBalance,'10.00');
  assert.equal(out.cashflowDaily.length,30);
  for(const day of out.cashflowDaily){assert.match(day.date,/^\d{4}-\d{2}-\d{2}$/);assert.match(day.income,money);assert.match(day.expense,money);}
  for(const b of out.branchBalances){assert.equal(typeof b.branchId,'string');assert.equal(typeof b.name,'string');assert.match(b.balance,money);}
  assert.ok(out.topExpenseCategories.length<=5&&out.topExpenseCategories.every(c=>money.test(c.amount)));
  assert.ok(Number.isInteger(out.pendingApprovals)&&Number.isInteger(out.overdueAdvances));
  assert.equal(demoAugment('GET','/dashboard',{cashflowDaily:[]}).branchBalances,undefined,'ответ настоящего backend не трогаем');
});

test('согласование: очередь, согласование, отклонение с обязательной причиной',async()=>{
  const queue=await demoResponse('GET','/approvals?status=pending');
  assert.ok(Array.isArray(queue.items)&&Number.isInteger(queue.total));
  const op=queue.items[0];
  for(const key of ['approvalStatus','approvedBy','approvedAt','approvalComment'])assert.ok(key in op,key);
  await assert.rejects(()=>demoResponse('POST',`/operations/${op.id}/reject`,{reason:' '}),/причину/);
  const approved=await demoResponse('POST',`/operations/${op.id}/approve`,{comment:'ок'});
  assert.equal(approved.approvalStatus,'approved');
});

test('вложения, авансы, купюры, инвентаризация и панель администратора',async()=>{
  const files=await demoResponse('GET','/attachments?ownerType=operation&ownerId=8101');
  assert.deepEqual(Object.keys(files[0]).sort(),['contentType','fileName','id','ownerId','ownerType','sha256','sizeBytes','uploadedAt','uploadedBy'].sort());
  const list=await demoResponse('GET','/advances?overdue=true');
  assert.ok(list.items.every(a=>a.overdue&&a.status==='issued'));
  const reported=(await demoResponse('GET','/advances?status=reported')).items[0];
  assert.match(reported.spent,money);assert.match(reported.remainder,money);
  const denoms=await demoResponse('GET','/denominations?currency=TJS');
  assert.ok(denoms.every(d=>money.test(d.value)&&['banknote','coin'].includes(d.kind)));
  const act=await demoResponse('POST','/cash-inventories',{cashboxId:1,denominations:[{value:'200.00',count:3}],commission:['Иванов И.']});
  assert.equal(act.actualBalance,'600.00');assert.match(act.difference,money);
  const health=await demoResponse('GET','/admin/health');
  assert.deepEqual(Object.keys(health).sort(),['activeSessions','appVersion','database','failedLogins24h','lastBackup','storage','usersWithoutCashAccess'].sort());
  assert.equal(await demoResponse('GET','/cashboxes'),undefined,'не-волна-1 маршруты идут на сервер');
  assert.ok(isWave1Route('POST','/operations/5/submit-approval'));assert.ok(!isWave1Route('GET','/operations/5'));
});
