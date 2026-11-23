import test from 'node:test';import assert from 'node:assert/strict';
import {demoOperations,demoCategories,demoParties,demoShiftPlan} from '../../scripts/demo-data.mjs';
test('demo cash operations match category direction counterparty and status',()=>{
  const rows=demoOperations('2026-11-12');let balance=0n;
  assert.equal(rows.filter(r=>r.status==='posted').length,21);assert.equal(rows.filter(r=>r.status==='draft').length,3);
  for(const row of rows){
    assert.ok(demoCategories.some(([name,type])=>name===row.category&&type===row.type));
    if(row.category==='Выручка от реализации')assert.equal(demoParties.find(([name])=>name===row.party)[1],'client');
    if(row.category==='Подотчётные суммы'||row.category==='Возврат подотчётных средств')assert.equal(demoParties.find(([name])=>name===row.party)[1],'employee');
    if(row.status==='posted'){const cents=BigInt(row.amount.replace('.',''));balance+=row.type==='income'?cents:-cents;assert.ok(balance>=0n);}
  }
});
test('demo seed opens sequential shifts with exact balances and leaves today open',()=>{
  const rows=demoOperations('2026-11-12'),shifts=demoShiftPlan(rows);
  assert.equal(shifts.length,21);
  assert.equal(shifts.filter(x=>x.close).length,20);
  assert.equal(shifts[0].openingBalance,'0.00');
  assert.equal(shifts[0].closingBalance,'25000.00');
  assert.equal(shifts[1].openingBalance,shifts[0].closingBalance);
  assert.equal(shifts.at(-1).date,'2026-11-12');
  assert.equal(shifts.at(-1).close,false);
  assert.ok(shifts.every(x=>/^\d+\.\d{2}$/.test(x.openingBalance)&&/^\d+\.\d{2}$/.test(x.closingBalance)));
});
