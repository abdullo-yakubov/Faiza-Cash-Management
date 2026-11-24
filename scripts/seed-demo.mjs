import {demoCategories,demoParties,demoOperations,demoCashboxes,demoBranchOperations,demoTransfer,demoDiscrepancy,demoDebts} from './demo-data.mjs';
import {api,setToken} from '../frontend/src/services/api.js';
import fs from 'node:fs';
import crypto from 'node:crypto';
// FG7-024: synthetic demo network for an EMPTY local educational database only. No production hook.
// Душанбе / Худжанд / Бохтар cashboxes with initial balances, 20 days of shifts, a transfer,
// debts with a partial payment and a responsible representative, representative receipts,
// one closed shift with a shortage (for the Dashboard "attention" block) and 3 drafts for today.
const configPath='.runtime/local.json';
const config=fs.existsSync(configPath)?JSON.parse(fs.readFileSync(configPath,'utf8')):{};
const adminLogin=process.env.FAIZA_ADMIN_LOGIN||config.Seed__AdminLogin||'admin';
const adminPassword=process.env.FAIZA_ADMIN_PASSWORD||config.Seed__AdminPassword;
if(!adminPassword)throw new Error('Задайте FAIZA_ADMIN_PASSWORD локального администратора.');
const base=process.env.FAIZA_API||'http://localhost:5080';const nativeFetch=globalThis.fetch;
globalThis.fetch=(url,options)=>nativeFetch(new URL(url,base),options);
const login=await api('/auth/login',{method:'POST',body:{login:adminLogin,password:adminPassword}});setToken(login.accessToken);
if(login.user.mustChangePassword){
 if(!process.argv.includes('--bootstrap')){console.log('Сначала смените первоначальный пароль в интерфейсе и задайте FAIZA_ADMIN_PASSWORD. Режим --bootstrap доступен только для явно запрошенной первоначальной подготовки учебной базы.');process.exit(1);}
 const newPassword=crypto.randomBytes(16).toString('hex')+'Aa1';
 await api('/auth/change-password',{method:'POST',body:{oldPassword:adminPassword,newPassword}});
 config.Seed__AdminLogin=adminLogin;config.Seed__AdminPassword=newPassword;fs.mkdirSync('.runtime',{recursive:true});fs.writeFileSync(configPath,JSON.stringify(config,null,2),{mode:0o600});
 console.log('Пароль локального администратора изменён. Данные доступа сохранены только в .runtime/local.json.');
}
if((await api('/cashboxes')).length){console.log('Наполнение пропущено: в базе уже есть кассы. Существующие записи не изменялись.');process.exit(0);}

const today=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Dushanbe'}).format(new Date());
const shift=(days)=>new Date(Date.parse(today+'T00:00:00Z')+days*86400000).toISOString().slice(0,10);
const start=shift(-20);
const currencies=await api('/currencies');
for(const [name,type]of demoCategories)await api('/categories',{method:'POST',body:{name,type,parentId:null}});
for(const [name,kind]of demoParties)await api('/counterparties',{method:'POST',body:{name,kind,taxCode:null,contact:null}});
const cats=await api('/categories'),parties=await api('/counterparties');
const category=(name,type)=>cats.find(c=>c.name===name&&c.type===type)?.id??(()=>{throw new Error('Нет категории '+name);})();
const party=index=>parties.find(p=>p.name===demoParties[index][0]).id;

const boxes={};
for(const item of demoCashboxes){
 const created=await api('/cashboxes',{method:'POST',body:{code:item.code,name:item.name,currencyId:currencies[0].id,balanceLimit:item.balanceLimit,branchId:item.branchId}});
 await api(`/cashboxes/${created.id}/opening`,{method:'POST',body:{date:start,amount:item.opening}});
 boxes[item.code]=created.id;
}
const representative=await api('/users',{method:'POST',body:{login:'demo-agent',fullName:'Рахимов Далер (демо)',password:crypto.randomBytes(16).toString('hex')+'Aa1',roles:['manager'],mustChangePassword:true}});

// Main cashbox history comes from the shared demo plan; its first row is replaced by the initial balance above.
const mainRows=demoOperations(today).filter(row=>row.category!=='Начальный остаток').map(row=>({...row,box:'DSH-MAIN'}));
const branchRows=demoBranchOperations.map(row=>({box:row.box,date:shift(row.day),type:row.type,category:row.category,party:demoParties[row.party][0],amount:row.amount,description:row.description,status:'posted'}));
const rows=[...mainRows,...branchRows];
const balance=async(box,date)=>(await api(`/cashboxes/${boxes[box]}/balance?date=${date}`)).balance;
const post=async(row)=>{
 const partyId=row.party?parties.find(p=>p.name===row.party).id:null;
 const draft=await api('/operations',{method:'POST',body:{cashboxId:boxes[row.box],date:row.date,type:row.type,amount:row.amount,description:row.description,categoryId:category(row.category,row.type),counterpartyId:partyId,documentTypeId:null,requestKey:crypto.randomUUID()}});
 if(row.status==='posted')return api(`/operations/${draft.id}/post`,{method:'POST',body:{expectedVersion:draft.version}});
 return draft;
};
const minus=(a,b)=>{const c=v=>{const [w,f='']=String(v).replace('-','').split('.');return (String(v).startsWith('-')?-1n:1n)*(BigInt(w)*100n+BigInt((f+'00').slice(0,2)));};const r=c(a)-c(b);const abs=r<0n?-r:r;return (r<0n?'-':'')+(abs/100n)+'.'+String(abs%100n).padStart(2,'0');};

for(let day=-19;day<=0;day++){
 const date=shift(day);
 const involved=new Set(rows.filter(r=>r.date===date).map(r=>r.box));
 if(day===demoTransfer.day){involved.add(demoTransfer.from);involved.add(demoTransfer.to);}
 if(day===0)for(const code of Object.keys(boxes))involved.add(code);
 const opened={};
 for(const code of involved)opened[code]=await api('/shifts/open',{method:'POST',body:{cashboxId:boxes[code],date,openingBalance:await balance(code,date)}});
 for(const row of rows.filter(r=>r.date===date))await post(row);
 if(day===demoTransfer.day)await api('/transfers',{method:'POST',body:{sourceCashboxId:boxes[demoTransfer.from],targetCashboxId:boxes[demoTransfer.to],date,amount:demoTransfer.amount,description:demoTransfer.description,requestKey:crypto.randomUUID()}});
 if(day<0)for(const code of involved){
  const expected=await balance(code,date);
  const counted=code===demoDiscrepancy.box&&day===demoDiscrepancy.day?minus(expected,demoDiscrepancy.difference.replace('-','')):expected;
  await api(`/shifts/${opened[code].id}/close`,{method:'POST',body:{closingBalance:counted}});
 }
}

for(const debt of demoDebts){
 const created=await api('/settlements',{method:'POST',body:{counterpartyId:party(debt.party),cashboxId:boxes[debt.box],representativeId:debt.representative?representative.id:null,direction:debt.direction,basisType:debt.basisType,basisReference:debt.basisReference,totalAmount:debt.totalAmount,dueDate:shift(debt.dueOffset)}});
 if(debt.payment){
  const payment=await post({box:debt.box,date:today,type:'income',category:'Выручка от реализации',party:demoParties[debt.party][0],amount:debt.payment,description:`Частичная оплата: ${debt.basisReference}`,status:'posted'});
  await api(`/settlements/${created.id}/allocate`,{method:'POST',body:{operationId:payment.id,amount:debt.payment}});
 }
}
const collected=await api('/representative-receipts',{method:'POST',body:{representativeId:representative.id,counterpartyId:party(0),basisType:'order',basisReference:'ЗАКАЗ-1050 (демо)',amount:'450.00',cashboxId:boxes['KHJ-01'],requestKey:crypto.randomUUID()}});
const handed=await api('/representative-receipts',{method:'POST',body:{representativeId:representative.id,counterpartyId:party(0),basisType:'delivery',basisReference:'ДОСТАВКА-12 (демо)',amount:'300.00',cashboxId:boxes['KHJ-01'],requestKey:crypto.randomUUID()}});
await api(`/representative-receipts/${handed.id}/handover`,{method:'POST'});
console.log(`Учебное наполнение завершено: 3 кассы (Душанбе, Худжанд, Бохтар) с начальными остатками, смены за 20 дней (одна с недостачей ${demoDiscrepancy.difference}), межкассовый перевод, 3 задолженности (частичная оплата, просрочка, представитель), квитанции представителя #${collected.id} и #${handed.id}, 3 черновика и открытые смены на сегодня.`);
