// FG7-013: browser journey against the dedicated loopback *_test PostgreSQL database.
// UI performs every business step; the API is used only to prepare fixtures and to verify effects.
// Run from faiza-cash after `dotnet build`: node tests/e2e.mjs
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,spawnSync} from 'node:child_process';
import playwright from '../frontend/node_modules/@playwright/test/index.js';
const {chromium}=playwright;

process.chdir(path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'));

const cfg=JSON.parse(fs.readFileSync('.runtime/local.json','utf8'));
const pairs=Object.fromEntries(cfg.ConnectionStrings__CashDb.split(';').map(value=>{const at=value.indexOf('=');return [value.slice(0,at),value.slice(at+1)];}));
const testDb=`${pairs.Database}_test`,apiBase='http://127.0.0.1:5082',webBase='http://127.0.0.1:5174';
assert.ok(['127.0.0.1','localhost','::1'].includes(pairs.Host),'E2E requires a loopback database');
assert.match(testDb,/^faiza_[a-z0-9]+_test$/,'E2E requires the dedicated _test database');
const psql='C:/Program Files/PostgreSQL/18/bin/psql.exe';
const results=[];let apiServer,webServer,browser;
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const waitFor=async url=>{for(let i=0;i<160;i++){try{if((await fetch(url)).ok)return;}catch{}await sleep(250);}throw new Error(`Timed out waiting for ${url}`);};
const runSql=sql=>{const result=spawnSync(psql,['-h',pairs.Host,'-p',pairs.Port||'5432','-U',pairs.Username,'-d',testDb,'-w','-At','-v','ON_ERROR_STOP=1'],{input:sql,encoding:'utf8',env:{...process.env,PGPASSWORD:pairs.Password}});assert.equal(result.status,0,result.stderr);return result.stdout.trim();};
let token;
async function request(path,{method='GET',body,auth=token}={}){const response=await fetch(apiBase+'/api'+path,{method,headers:{'Content-Type':'application/json','X-Faiza-Request':'1',...(auth?{Authorization:`Bearer ${auth}`}:{})},body:body===undefined?undefined:JSON.stringify(body)});if(!response.ok)throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);return response.status===204?null:response.json();}
async function test(name,fn){try{await fn();results.push({name,status:'passed'});console.log(`PASS ${name}`);}catch(error){results.push({name,status:'failed',error:error.message});console.log(`FAIL ${name}: ${error.message}`);}}
async function login(page,login,password){await page.goto(webBase);await page.locator('input[name="login"]').fill(login);await page.locator('input[name="password"]').fill(password);await page.getByRole('button',{name:'Войти в систему'}).click();await page.waitForSelector('.app-shell');}
async function newDocument(page,kind,amount,category,description){
  await page.locator('a[href="#/overview"]').click();
  await page.getByRole('button',{name:kind==='income'?'Новый приход':'Новый расход'}).click();
  await page.locator('#field-amount').fill(amount);await page.selectOption('#field-categoryId',{label:category});await page.locator('#field-description').fill(description);
  await page.getByRole('button',{name:'Создать черновик'}).click();
}
const lockFile='.runtime/database-tests.lock';
try{
  fs.closeSync(fs.openSync(lockFile,'wx'));
  process.once('exit',()=>{try{fs.unlinkSync(lockFile);}catch{}});
  runSql('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
  const e2eAdminPassword=crypto.randomBytes(18).toString('hex')+'Aa1',adminPassword='AdminE2E2026A';
  apiServer=spawn('dotnet',['backend/src/FaizaCash.Api/bin/Debug/net10.0/FaizaCash.Api.dll'],{env:{...process.env,...cfg,ConnectionStrings__CashDb:cfg.ConnectionStrings__CashDb.replace(/Database=([^;]+)/i,`Database=${testDb}`),ASPNETCORE_URLS:apiBase,ASPNETCORE_ENVIRONMENT:'Test',Seed__AdminLogin:'e2e-admin',Seed__AdminPassword:e2eAdminPassword,Jwt__Key:crypto.randomBytes(48).toString('hex'),Jwt__Issuer:'FaizaCash',Jwt__Audience:'FaizaCash',Logs__Directory:'.runtime/e2e-api-logs',Backups__IntervalHours:'0'},stdio:['ignore',fs.openSync('.runtime/e2e-api.log','w'),'inherit']});
  await waitFor(apiBase+'/health');
  const initial=await request('/auth/login',{method:'POST',auth:null,body:{login:'e2e-admin',password:e2eAdminPassword}});token=initial.accessToken;
  await request('/auth/change-password',{method:'POST',body:{oldPassword:e2eAdminPassword,newPassword:adminPassword}});
  const currencies=await request('/currencies');
  const box=await request('/cashboxes',{method:'POST',body:{code:'E2E',name:'E2E cashbox',currencyId:currencies[0].id,balanceLimit:null,branchId:1}});
  const foreign=await request('/cashboxes',{method:'POST',body:{code:'E2E-FOREIGN',name:'E2E foreign box',currencyId:currencies[0].id,balanceLimit:null,branchId:2}});
  const openingBox=await request('/cashboxes',{method:'POST',body:{code:'E2E-OPEN',name:'E2E opening box',currencyId:currencies[0].id,balanceLimit:null,branchId:3}});
  await request('/categories',{method:'POST',body:{name:'E2E income',type:'income',parentId:null}});await request('/categories',{method:'POST',body:{name:'E2E expense',type:'expense',parentId:null}});
  await request('/counterparties',{method:'POST',body:{name:'E2E Клиент',kind:'client',taxCode:null,contact:null}});
  const cashierPassword='CashierE2E2026A',accountantPassword='AccountantE2E2026A';
  const cashier=await request('/users',{method:'POST',body:{login:'e2e-cashier',fullName:'E2E Cashier',password:cashierPassword,roles:['cashier'],mustChangePassword:false}});
  const accountant=await request('/users',{method:'POST',body:{login:'e2e-accountant',fullName:'E2E Accountant',password:accountantPassword,roles:['accountant'],mustChangePassword:false}});
  await request('/access',{method:'POST',body:{userId:cashier.id,cashboxId:box.id}});await request('/access',{method:'POST',body:{userId:accountant.id,cashboxId:box.id}});
  const postedCount=(description)=>runSql(`SELECT count(*) FROM "Operations" WHERE "Description"='${description}' AND "Status"='posted'`);
  const draftCount=(description)=>runSql(`SELECT count(*) FROM "Operations" WHERE "Description"='${description}'`);
  webServer=spawn(process.execPath,['node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','5174','--strictPort'],{cwd:'frontend',env:{...process.env,API_PROXY:apiBase},stdio:['ignore',fs.openSync('.runtime/e2e-vite.log','w'),'inherit']});
  await waitFor(webBase);
  browser=await chromium.launch({headless:true});

  await test('negative: browser rejects incorrect credentials',async()=>{const page=await browser.newPage();await page.goto(webBase);await page.locator('input[name="login"]').fill('e2e-cashier');await page.locator('input[name="password"]').fill('wrong-password');await page.getByRole('button',{name:'Войти в систему'}).click();await page.getByRole('alert').waitFor();assert.equal(await page.locator('.app-shell').count(),0);await page.close();});

  await test('admin sets a one-time cashbox initial balance through the UI',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-admin',adminPassword);
    await page.locator('a[href="#/cashboxes"]').click();
    const card=page.locator('.cash-card').filter({hasText:'E2E opening box'});
    await card.getByRole('button',{name:'Задать начальный остаток'}).click();
    const dialog=page.getByRole('dialog');await dialog.locator('input[name="amount"]').fill('1000');
    await dialog.getByRole('button',{name:'Задать начальный остаток'}).click();
    await page.getByText('Начальный остаток кассы задан.').waitFor();
    const listed=(await request('/cashboxes')).find(x=>x.id===openingBox.id);
    assert.equal(listed.openingAmount,'1000.00');
    await page.locator('.cash-card').filter({hasText:'E2E opening box'}).getByText(/Начальный остаток: 1.000,00/).waitFor();
    await page.close();
  });

  await test('negative: cashier cannot reach a foreign cashbox by URL',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-cashier',cashierPassword);
    await page.goto(`${webBase}/#/operations?box=${foreign.id}`);await page.waitForSelector('.app-shell');
    await page.waitForFunction(id=>location.hash.includes('box='+id),box.id);
    assert.equal(await page.getByText('E2E foreign box').count(),0,'foreign cashbox name must never be rendered');
    await page.close();
  });

  await test('negative: posting without an open shift is refused and leaves a draft',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-cashier',cashierPassword);
    await newDocument(page,'income','5.00','E2E income','E2E no shift');
    await page.getByRole('button',{name:'Провести'}).click();await page.getByRole('button',{name:'Подтвердить проведение'}).click();
    await page.getByText(/Сначала откройте кассовую смену/).waitFor();
    assert.equal(postedCount('E2E no shift'),'0');assert.equal(draftCount('E2E no shift'),'1');
    await page.close();
  });

  await test('negative: an amount with three decimals is rejected before any draft is created',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-cashier',cashierPassword);
    await newDocument(page,'income','0.001','E2E income','E2E invalid amount');
    await page.locator('#error-amount').filter({hasText:/не более двух знаков/}).waitFor();
    assert.equal(draftCount('E2E invalid amount'),'0');
    await page.close();
  });

  await test('cashier: open shift → income 1 000 (double submit) → expense 250 → balance 750 → close shift',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-cashier',cashierPassword);
    await page.locator('a[href="#/shifts"]').click();
    const opening=page.getByLabel(/Начальный остаток/);await opening.fill('0');
    await page.getByRole('button',{name:'Открыть смену'}).click();await page.getByRole('status').filter({hasText:'Смена открыта'}).waitFor();
    await newDocument(page,'income','1000.00','E2E income','E2E income');
    await page.getByRole('button',{name:'Провести'}).click();
    await page.getByRole('button',{name:'Подтвердить проведение'}).evaluate(button=>{button.click();button.click();});
    await page.getByText('Документ проведён. Остаток обновлён.').waitFor();
    assert.equal(postedCount('E2E income'),'1','double submit must post exactly once');
    await page.getByRole('button',{name:'Закрыть диалог'}).click();
    await newDocument(page,'expense','250.00','E2E expense','E2E expense');
    await page.getByRole('button',{name:'Провести'}).click();await page.getByRole('button',{name:'Подтвердить проведение'}).click();
    await page.getByText('Документ проведён. Остаток обновлён.').waitFor();
    await page.getByRole('button',{name:'Закрыть диалог'}).click();
    await page.locator('a[href="#/overview"]').click();await page.getByText('750,00').first().waitFor();
    assert.equal((await request(`/cashboxes/${box.id}/balance`)).balance,'750.00');
    await page.locator('a[href="#/shifts"]').click();
    await page.getByLabel(/Фактический остаток/).fill('750');await page.getByRole('button',{name:'Закрыть смену'}).click();await page.getByRole('status').filter({hasText:'Смена закрыта'}).waitFor();
    await page.screenshot({path:'.runtime/e2e-cashier-closed-shift.png',fullPage:true});
    const shift=(await request(`/shifts?cashboxId=${box.id}`))[0];
    assert.equal(shift.status,'closed');assert.equal(shift.closingDifference,'0.00');
    await page.close();
  });

  await test('accountant on 390×844: finds the closed shift, cashbook shows 750 and CSV export matches',async()=>{
    const page=await browser.newPage({viewport:{width:390,height:844}});await login(page,'e2e-accountant',accountantPassword);
    await page.goto(`${webBase}/#/shifts`);await page.getByText(/Закрыта/).first().waitFor();
    await page.getByText(/закрытие 750,00/).waitFor();
    await page.goto(`${webBase}/#/reports`);await page.getByText('Кассовая книга').first().waitFor();await page.getByText('750,00').first().waitFor();
    const download=page.waitForEvent('download');await page.getByRole('button',{name:'Скачать CSV'}).click();
    const file=await (await download).path();const lines=fs.readFileSync(file,'utf8').trim().split(/\r?\n/);
    assert.equal(lines.at(-1).split(';').at(-1),'750.00');
    await page.screenshot({path:'.runtime/e2e-accountant-mobile.png',fullPage:true});
    await page.close();
  });

  await test('accountant creates a debt with a responsible representative through the UI',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-accountant',accountantPassword);
    await page.goto(`${webBase}/#/debts`);await page.getByRole('button',{name:'Создать задолженность'}).click();
    const form=page.locator('form.v5-form');
    await form.getByLabel('Касса').selectOption({label:'E2E cashbox · Душанбе'}).catch(async()=>{await form.getByLabel('Касса').selectOption(String(box.id));});
    await form.getByLabel('Контрагент').selectOption({label:'E2E Клиент'});
    await form.getByLabel('Ответственный представитель (необязательно)').selectOption({label:'E2E Accountant'});
    await form.getByLabel('Документ').fill('E2E-ORDER-1');await form.getByLabel(/Сумма/).fill('300');
    await form.getByRole('button',{name:'Сохранить'}).click();
    await page.getByText('Задолженность создана').waitFor();
    await page.locator('tr').filter({hasText:'E2E-ORDER-1'}).filter({hasText:'E2E Accountant'}).waitFor();
    await page.close();
  });

  await test('accessibility (WCAG 2.2 A/AA, automated part): skip link, names, visible keyboard focus, landmarks, reduced motion',async()=>{
    const page=await browser.newPage({viewport:{width:1280,height:800}});await page.emulateMedia({reducedMotion:'reduce'});
    await login(page,'e2e-admin',adminPassword);
    const problems=[];
    for(const route of ['overview','operations','shifts','cashboxes','debts','reports','audit','backups','users']){
      await page.goto(`${webBase}/#/${route}`);await page.waitForSelector('#main-title');await page.waitForLoadState('networkidle');
      const found=await page.evaluate(()=>{
        const visible=el=>{const r=el.getBoundingClientRect(),s=getComputedStyle(el);return r.width>0&&r.height>0&&s.visibility!=='hidden'&&s.display!=='none';};
        const name=el=>(el.getAttribute('aria-label')||(el.getAttribute('aria-labelledby')||'').split(' ').map(id=>document.getElementById(id)?.textContent||'').join(' ')||[...(el.labels||[])].map(l=>l.textContent).join(' ')||el.getAttribute('title')||(['BUTTON','A'].includes(el.tagName)?el.textContent:'')||'').trim();
        const issues=[];
        for(const el of document.querySelectorAll('input:not([type=hidden]),select,textarea,button,a[href],[role=button]'))if(visible(el)&&!name(el))issues.push(`${el.tagName.toLowerCase()}${el.id?'#'+el.id:''}${el.name?'[name='+el.name+']':''} without accessible name`);
        if(document.querySelectorAll('main').length!==1)issues.push('expected exactly one <main>');
        if(document.querySelectorAll('h1').length!==1)issues.push('expected exactly one <h1>');
        if(document.documentElement.lang!=='ru')issues.push('html lang is not ru');
        const slow=document.getAnimations().filter(a=>{const t=a.effect?.getTiming?.();return t&&Number(t.duration)>10&&t.iterations!==0;});
        if(slow.length)issues.push(`${slow.length} animations run despite prefers-reduced-motion`);
        return issues;
      });
      problems.push(...found.map(x=>`${route}: ${x}`));
    }
    await page.goto(`${webBase}/#/overview`);await page.waitForSelector('#main-title');await page.locator('body').click({position:{x:640,y:2}});
    await page.keyboard.press('Tab');
    const first=await page.evaluate(()=>({text:document.activeElement?.textContent?.trim(),visible:document.activeElement?.getBoundingClientRect().top>=0}));
    if(first.text!=='Перейти к содержимому'||!first.visible)problems.push('first Tab stop is not a visible "Перейти к содержимому" link: '+JSON.stringify(first));
    else{await page.keyboard.press('Enter');if(await page.evaluate(()=>document.activeElement?.id)!=='main-title')problems.push('skip link does not move focus to the page heading');}
    for(let i=0;i<15;i++){
      await page.keyboard.press('Tab');
      const focus=await page.evaluate(()=>{const el=document.activeElement,s=getComputedStyle(el);return {tag:el.tagName,text:(el.textContent||el.getAttribute('aria-label')||'').trim().slice(0,30),indicator:(s.outlineStyle!=='none'&&parseFloat(s.outlineWidth)>=1)||s.boxShadow!=='none'};});
      if(focus.tag!=='BODY'&&!focus.indicator)problems.push(`no visible focus indicator on ${focus.tag} "${focus.text}"`);
    }
    await page.close();
    assert.deepEqual(problems,[]);
  });
} catch(error){
  console.error(error.message);process.exitCode=1;
} finally {
  if(browser)await browser.close();
  for(const child of [webServer,apiServer])if(child&&!child.killed)child.kill();
  fs.writeFileSync('.runtime/e2e-results.json',JSON.stringify({time:new Date().toISOString(),database:'dedicated loopback _test database',results},null,2));
  console.log(`E2E checks: ${results.filter(x=>x.status==='passed').length} passed, ${results.filter(x=>x.status!=='passed').length} failed.`);
  if(results.some(x=>x.status!=='passed')||results.length===0)process.exitCode=1;
}
