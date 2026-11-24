import test from 'node:test';
import assert from 'node:assert/strict';
import {can,canTouchMoney,roleDescriptions,ROLES} from '../src/utils/permissions.js';

const user=(role,id='u-'+role)=>({id,roles:[role]});
const admin=user('admin'),accountant=user('accountant'),cashier=user('cashier'),manager=user('manager'),auditor=user('auditor');

test('admin не выполняет финансовых операций, но видит всё и настраивает систему',()=>{
  for(const action of ['operation.create','operation.post','operation.reverse','shift.manage','transfer.create','receipt.accept','debt.allocate','advance.manage','inventory.create','operation.submitApproval','operation.approve'])
    assert.equal(can(admin,action),false,action);
  for(const action of ['cash.view','audit.view','attachments.view','advances.view','inventory.view','users.manage','backups.manage','settings.manage','rates.manage','admin.health','logo.manage','avatar.manage'])
    assert.equal(can(admin,action),true,action);
  assert.equal(canTouchMoney(admin),false);
});

test('строки новой матрицы волны 1',()=>{
  const expect={
    'operation.submitApproval':[false,true,true,false,false],
    'operation.approve':[false,false,false,true,false],
    'attachment.manage':[false,true,true,true,false],
    'attachments.view':[true,true,true,true,true],
    'advance.manage':[false,true,false,false,false],
    'advances.view':[true,true,true,true,true],
    'inventory.create':[false,true,false,true,false],
    'inventory.view':[true,true,true,true,true],
    'rates.manage':[true,true,false,false,false],
    'admin.health':[true,false,false,false,false],
    'logo.manage':[true,false,false,false,false],
    'avatar.manage':[true,true,true,true,true]
  };
  const people=[admin,accountant,cashier,manager,auditor];
  for(const [action,row] of Object.entries(expect))assert.deepEqual(people.map(p=>can(p,action)),row,action);
});

test('кассир работает только со своими черновиками, бухгалтер — с любыми',()=>{
  const own={authorId:cashier.id,status:'draft',approvalStatus:'none'},foreign={authorId:'someone',status:'draft',approvalStatus:'none'};
  assert.equal(can(cashier,'operation.post',own),true);
  assert.equal(can(cashier,'operation.post',foreign),false);
  assert.equal(can(accountant,'operation.post',foreign),true);
  assert.equal(can(accountant,'operation.edit',{...foreign,status:'posted'}),false);
  assert.equal(can(cashier,'operation.submitApproval',own),true);
  assert.equal(can(cashier,'operation.submitApproval',{...own,approvalStatus:'pending'}),false);
  assert.equal(can(cashier,'operation.reverse'),false);
});

test('руководитель не согласует собственный расход и только ожидающий',()=>{
  const pending={authorId:'other',status:'draft',approvalStatus:'pending'};
  assert.equal(can(manager,'operation.approve',pending),true);
  assert.equal(can(manager,'operation.approve',{...pending,authorId:manager.id}),false);
  assert.equal(can(manager,'operation.approve',{...pending,approvalStatus:'none'}),false);
});

test('руководитель управляет вложениями только у квитанций; кассир — у своих документов',()=>{
  assert.equal(can(manager,'attachment.manage',{ownerType:'receipt',authorId:'x'}),true);
  assert.equal(can(manager,'attachment.manage',{ownerType:'operation',authorId:'x'}),false);
  assert.equal(can(cashier,'attachment.manage',{ownerType:'operation',authorId:cashier.id}),true);
  assert.equal(can(cashier,'attachment.manage',{ownerType:'operation',authorId:'x'}),false);
});

test('несколько ролей объединяются, неизвестные действия и пустой пользователь запрещены',()=>{
  const both={id:'u',roles:['admin','accountant']};
  assert.equal(can(both,'operation.create'),true);
  assert.equal(can(null,'cash.view'),false);
  assert.equal(can(accountant,'nonexistent'),false);
  assert.ok(ROLES.every(role=>roleDescriptions[role].length>20));
});
