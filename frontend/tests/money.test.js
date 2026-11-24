import test from 'node:test';
import assert from 'node:assert/strict';
import {toCents,fromCents,tryCents,addAmounts,subtractAmounts,compareAmounts,parseCount,denominationTotal,reconcile,advanceTotals,absAmount} from '../src/utils/money.js';

test('строки-суммы переводятся в целые дирамы и обратно без потерь',()=>{
  assert.equal(toCents('1500.00'),150000n);
  assert.equal(toCents('0.1'),10n);
  assert.equal(toCents('1 500,25'),150025n);
  assert.equal(toCents('-50.5'),-5050n);
  assert.equal(toCents(''),0n);
  assert.equal(fromCents(150025n),'1500.25');
  assert.equal(fromCents(-5n),'-0.05');
  assert.equal(fromCents(toCents('9999999999999999.99')),'9999999999999999.99');
  assert.throws(()=>toCents('1.001'));
  assert.throws(()=>toCents('1e3'));
  assert.equal(tryCents('abc'),null);
});

test('сложение не страдает от ошибок float (0.1 + 0.2)',()=>{
  assert.equal(addAmounts('0.10','0.20'),'0.30');
  assert.equal(addAmounts(['9999999999999999.99','0.01']),'10000000000000000.00');
  assert.equal(subtractAmounts('100.00','150.50'),'-50.50');
  assert.equal(compareAmounts('10.00','9.99'),1);
  assert.equal(compareAmounts('10','10.00'),0);
  assert.equal(absAmount('-12.30'),'12.30');
});

test('калькулятор купюр считает итог и находит неверные количества',()=>{
  const result=denominationTotal([{value:'200.00',count:'3'},{value:'0.50',count:7},{value:'0.05',count:''},{value:'100.00',count:'2.5'}]);
  assert.equal(result.total,'603.50');
  assert.deepEqual(result.invalid,[3]);
  assert.equal(result.lines[1].amount,'3.50');
  assert.equal(parseCount('-1'),null);
  assert.equal(parseCount(' 12 '),12);
  assert.equal(parseCount('12345678'),null);
});

test('сверка факта с расчётом определяет недостачу и излишек',()=>{
  assert.deepEqual(reconcile('950.00','1000.00'),{difference:'-50.00',kind:'shortage'});
  assert.deepEqual(reconcile('1000.10','1000.00'),{difference:'0.10',kind:'surplus'});
  assert.deepEqual(reconcile('1000','1000.00'),{difference:'0.00',kind:'match'});
});

test('авансовый отчёт: остаток, перерасход и вид закрывающей операции',()=>{
  assert.deepEqual(advanceTotals('1000.00',[{amount:'300.10'},{amount:'199.90'}]),{spent:'500.00',remainder:'500.00',overspent:false,settlement:'return'});
  assert.deepEqual(advanceTotals('1000.00',[{amount:'1200.00'}]),{spent:'1200.00',remainder:'-200.00',overspent:true,settlement:'reimburse'});
  assert.equal(advanceTotals('1000.00',[{amount:'1000'},{amount:'не число'}]).settlement,'none');
});
