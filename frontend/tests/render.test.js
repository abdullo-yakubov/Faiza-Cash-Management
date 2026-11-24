import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {createServer} from 'vite';

test('users table renders roles and empty results without join crashes',async()=>{
  const server=await createServer({configFile:false,server:{middlewareMode:true},optimizeDeps:{noDiscovery:true}});
  try{
    const {UsersPage}=await server.ssrLoadModule('/src/pages/UsersPage.jsx');
    const user={id:'u1',login:'admin',fullName:'Администратор',active:true,mustChangePassword:false,roles:['admin']};
    const assigned={userId:'u2',cashboxId:7};
    const props={user,boxes:[{id:7,name:'Основная касса'}],access:[assigned],onModal(){},onToggle(){},onRevoke(){},onPage(){}};
    const html=renderToStaticMarkup(React.createElement(UsersPage,{...props,data:{items:[user,{...user,id:'u2',login:'no-role',roles:[]}],page:1,total:2}}));
    assert.match(html,/Администратор/);assert.match(html,/Роль не назначена/);assert.doesNotMatch(html,/undefined/);
    assert.match(html,/Основная касса/);assert.match(html,/aria-label="Отозвать доступ: Администратор — Основная касса"/);
    const resetHtml=renderToStaticMarkup(React.createElement(UsersPage,{...props,onResetPassword(){},data:{items:[user,{...user,id:'u2',login:'no-role',fullName:'Сотрудник',roles:[]}],page:1,total:2}}));
    assert.match(resetHtml,/Сбросить пароль/);
    assert.match(renderToStaticMarkup(React.createElement(UsersPage,{...props,data:{items:[],page:1,total:0}})),/Сотрудники не найдены/);
    assert.throws(()=>renderToStaticMarkup(React.createElement(UsersPage,{...props,data:{items:[{id:10,amount:'100.00'}],page:1,total:1}})),/неполные данные/);
  }finally{await server.close();}
});
test('document dialog keeps actions outside its scroll area and respects draft ownership',async()=>{
  const server=await createServer({configFile:false,server:{middlewareMode:true},optimizeDeps:{noDiscovery:true}});
  try{
    const {OperationDialog}=await server.ssrLoadModule('/src/components/OperationDialog.jsx');
    const {PasswordInput}=await server.ssrLoadModule('/src/components/PasswordInput.jsx');
    const user={id:'u1',roles:['cashier']};const row={id:1,cashboxId:1,type:'income',date:'2026-11-12',status:'draft',amount:'1.00',description:'<script>danger()</script>',authorId:'u2',categoryId:1};
    const base={modal:{kind:'details',row},boxes:[{id:1,name:'Основная касса',active:true}],currency:'TJS',user,roles:[],busy:false,onClose(){},onSave(){},onModal(){},onDownload(){}};
    const foreign=renderToStaticMarkup(React.createElement(OperationDialog,base));
    assert.match(foreign,/role="dialog"/);assert.match(foreign,/aria-modal="true"/);assert.match(foreign,/class="dialog-footer"/);
    assert.doesNotMatch(foreign,/<script>/);assert.match(foreign,/&lt;script&gt;/);assert.doesNotMatch(foreign,/>Провести</);
    const own=renderToStaticMarkup(React.createElement(OperationDialog,{...base,modal:{kind:'details',row:{...row,authorId:'u1'}}}));assert.match(own,/>Провести</);
    const create=renderToStaticMarkup(React.createElement(OperationDialog,{...base,modal:{kind:'operation',type:'income',categories:[{id:1,name:'Категория',type:'income',active:true}],parties:[]}}));assert.match(create,/form="document-form"/);assert.match(create,/inputMode="decimal"/i);assert.match(create,/черновик/);
    const password=renderToStaticMarkup(React.createElement(PasswordInput,{name:'password',autoComplete:'new-password'}));
    assert.match(password,/type="password"/);assert.match(password,/autoComplete="new-password"/);assert.match(password,/type="button"/);assert.match(password,/aria-label="Показать пароль"/);assert.match(password,/aria-pressed="false"/);
    const reset=renderToStaticMarkup(React.createElement(OperationDialog,{...base,modal:{kind:'resetPassword',row:{id:'u2',fullName:'Сотрудник'}}}));
    assert.match(reset,/Сброс пароля сотрудника/);assert.match(reset,/Подтвердите пароль/);assert.match(reset,/Сбросить пароль/);
  }finally{await server.close();}
});
test('report export panel, debt representative filter and dashboard attention render accessibly',async()=>{
  const server=await createServer({configFile:false,server:{middlewareMode:true},optimizeDeps:{noDiscovery:true}});
  try{
    const {ReportExports}=await server.ssrLoadModule('/src/pages/ReportsPage.jsx');
    const boxes=[{id:7,name:'Основная касса',branchId:2,branch:'Худжанд'}];
    const html=renderToStaticMarkup(React.createElement(ReportExports,{box:'7',boxes,from:'2026-11-01',to:'2026-11-24',canViewDebts:true,busy:false,onDownload(){}}));
    for(const label of ['Отчёт по кассовым сменам: CSV','Отчёт по филиалу «Худжанд»: PDF','Межкассовые переводы: CSV','Задолженности и платежи: PDF','Торговые представители: CSV'])assert.match(html,new RegExp('aria-label="'+label+'"'));
    const cashier=renderToStaticMarkup(React.createElement(ReportExports,{box:'7',boxes,from:'2026-11-01',to:'2026-11-24',canViewDebts:false,busy:false,onDownload(){}}));
    assert.doesNotMatch(cashier,/Задолженности и платежи/);assert.doesNotMatch(cashier,/Торговые представители/);
    const {DebtsPage}=await server.ssrLoadModule('/src/pages/V5Pages.jsx');
    const debts=renderToStaticMarkup(React.createElement(DebtsPage,{parties:[],boxes,representatives:[{id:'r1',fullName:'Саидов Фарид'}],currency:'TJS',canWrite:true}));
    assert.ok(debts.includes('Загрузка'),'debts page renders its loading state without a ReferenceError');
  }finally{await server.close();}
});
