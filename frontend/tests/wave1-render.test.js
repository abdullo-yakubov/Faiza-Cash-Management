import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {createServer} from 'vite';

const h=React.createElement;
async function withServer(run){
  const server=await createServer({configFile:false,server:{middlewareMode:true},optimizeDeps:{noDiscovery:true},logLevel:'silent'});
  try{await run(path=>server.ssrLoadModule(path));}finally{await server.close();}
}

test('карточка документа: admin только смотрит, кассир отправляет крупный расход на согласование',()=>withServer(async load=>{
  const {OperationDialog}=await load('/src/components/OperationDialog.jsx');
  const row={id:5,cashboxId:1,type:'expense',date:'2026-11-20',status:'draft',amount:'12000.00',description:'Ремонт',authorId:'c1',approvalStatus:'none',version:1};
  const base={modal:{kind:'details',row},boxes:[{id:1,name:'Основная касса',active:true}],currency:'TJS',roles:[],busy:false,onClose(){},onSave(){},onModal(){},onDownload(){},onApproval(){},threshold:'5000.00'};
  const admin=renderToStaticMarkup(h(OperationDialog,{...base,user:{id:'a1',roles:['admin']}}));
  assert.doesNotMatch(admin,/>Провести</);assert.doesNotMatch(admin,/Изменить</);assert.doesNotMatch(admin,/Отправить на согласование/);
  const cashier=renderToStaticMarkup(h(OperationDialog,{...base,user:{id:'c1',roles:['cashier']}}));
  assert.match(cashier,/Отправить на согласование/);
  assert.match(cashier,/disabled=""[^>]*>Провести|title="Расход требует согласования руководителя"/);
  assert.match(cashier,/Сканы и фото документов/);
  const approved=renderToStaticMarkup(h(OperationDialog,{...base,modal:{kind:'details',row:{...row,approvalStatus:'approved',approvedBy:'Шарипов Д.'}},user:{id:'c1',roles:['cashier']}}));
  assert.match(approved,/Согласовано/);assert.doesNotMatch(approved,/Отправить на согласование/);
  const posted=renderToStaticMarkup(h(OperationDialog,{...base,modal:{kind:'details',row:{...row,status:'posted'}},user:{id:'a1',roles:['admin']}}));
  assert.doesNotMatch(posted,/Сторнировать/,'admin не сторнирует');
  const form=renderToStaticMarkup(h(OperationDialog,{...base,modal:{kind:'users'},user:{id:'a1',roles:['admin']}}));
  for(const label of ['Руководитель','Аудитор','Кассир','Бухгалтер'])assert.match(form,new RegExp(label));
  assert.match(form,/согласует расходы/);
}));

test('калькулятор купюр показывает итог, недостачу и подписи для экранного диктора',()=>withServer(async load=>{
  const {DenominationCalculator}=await load('/src/components/DenominationCalculator.jsx');
  const rows=[{value:'200.00',kind:'banknote',count:'4'},{value:'0.50',kind:'coin',count:'3'}];
  const html=renderToStaticMarkup(h(DenominationCalculator,{rows,onChange(){},expected:'1000.00'}));
  assert.match(html,/801,50/);assert.match(html,/Недостача/);assert.match(html,/198,50/);
  assert.match(html,/aria-label="Количество: 200,00 TJS"/);assert.match(html,/Банкноты/);assert.match(html,/Монеты/);
}));

test('новые экраны рендерят скелетоны загрузки без ошибок',()=>withServer(async load=>{
  const user={id:'m1',fullName:'Шарипов Джамшед',roles:['manager']};
  const boxes=[{id:1,name:'Основная касса',active:true,branch:'Душанбе'}];
  const pages=[
    ['/src/pages/CashierPage.jsx','CashierPage',{user:{...user,roles:['cashier']},boxes,box:'1',onBox(){},onOperation(){},onDetails(){}}],
    ['/src/pages/ApprovalsPage.jsx','ApprovalsPage',{user,boxes,onDetails(){}}],
    ['/src/pages/AdminPage.jsx','AdminPage',{onGrantAccess(){}}],
    ['/src/pages/AdvancesPage.jsx','AdvancesPage',{user,boxes,parties:[],categories:[]}],
    ['/src/pages/InventoryPage.jsx','InventoryPage',{user,boxes}],
    ['/src/pages/ExchangeRatesPage.jsx','ExchangeRatesPage',{user}],
    ['/src/pages/SettingsPage.jsx','SettingsPage',{user,org:{name:'ООО «Фаиза»',currency:'TJS',expenseApprovalThreshold:'5000.00'},onPassword(){}}]
  ];
  for(const [path,name,props] of pages){
    const html=renderToStaticMarkup(h((await load(path))[name],props));
    assert.doesNotMatch(html,/undefined|NaN/,name);
  }
  const cashier=renderToStaticMarkup(h((await load('/src/pages/CashierPage.jsx')).CashierPage,pages[0][2]));
  assert.match(cashier,/Приход/);assert.match(cashier,/Расход/);
  const settings=renderToStaticMarkup(h((await load('/src/pages/SettingsPage.jsx')).SettingsPage,pages[6][2]));
  assert.match(settings,/5 000,00|5 000,00|5 000,00/);assert.doesNotMatch(settings,/Загрузить логотип/,'руководитель не меняет логотип');
}));

test('панель руководителя: KPI-плитки ведут в отфильтрованные списки',()=>withServer(async load=>{
  const {ManagerOverview}=await load('/src/components/ManagerOverview.jsx');
  const d={totalBalance:'265535.90',periodTotals:{todayIncome:'1500.00',todayExpense:'200.00'},pendingApprovals:2,overdueAdvances:1,attention:[{code:'shift_discrepancy'}],
    cashflowDaily:[{date:'2026-11-23',income:'100.00',expense:'50.00'},{date:'2026-11-24',income:'0.10',expense:'0.20'}],
    branchBalances:[{branchId:'b1',name:'Душанбе',balance:'100.00'}],topExpenseCategories:[{categoryId:1,name:'Ремонт',amount:'50.00'}]};
  const html=renderToStaticMarkup(h(ManagerOverview,{d,draftCount:3}));
  assert.match(html,/href="#\/approvals"/);assert.match(html,/href="#\/advances\?filter=overdue"/);assert.match(html,/status=draft/);
  assert.match(html,/100,10/,'итог прихода считается в дирамах без ошибок float');
  assert.match(html,/Душанбе/);assert.match(html,/Ремонт/);
}));
