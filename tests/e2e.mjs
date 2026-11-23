// Browser journey against a disposable loopback PostgreSQL database.
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
const waitFor=async url=>{for(let i=0;i<120;i++){try{if((await fetch(url)).ok)return;}catch{}await sleep(250);}throw new Error(`Timed out waiting for ${url}`);};
const runSql=sql=>{const result=spawnSync(psql,['-h',pairs.Host,'-p',pairs.Port,'-U',pairs.Username,'-d',testDb,'-w','-v','ON_ERROR_STOP=1'],{input:sql,encoding:'utf8',env:{...process.env,PGPASSWORD:pairs.Password}});assert.equal(result.status,0,result.stderr);};
let token;
async function request(path,{method='GET',body,auth=token}={}){const response=await fetch(apiBase+'/api'+path,{method,headers:{'Content-Type':'application/json','X-Faiza-Request':'1',...(auth?{Authorization:`Bearer ${auth}`}:{})},body:body===undefined?undefined:JSON.stringify(body)});if(!response.ok)throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);return response.status===204?null:response.json();}
async function test(name,fn){try{await fn();results.push({name,status:'passed'});console.log(`PASS ${name}`);}catch(error){results.push({name,status:'failed',error:error.message});throw error;}}
async function login(page,login,password){await page.goto(webBase);await page.locator('input[name="login"]').fill(login);await page.locator('input[name="password"]').fill(password);await page.locator('form').filter({has:page.locator('input[name="login"]')}).getByRole('button').click();await page.waitForSelector('.app-shell');}
try{
  runSql('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
  const e2eAdminPassword=crypto.randomBytes(18).toString('hex')+'Aa1';
  apiServer=spawn('dotnet',['backend/src/FaizaCash.Api/bin/Debug/net10.0/FaizaCash.Api.dll'],{env:{...process.env,...cfg,ConnectionStrings__CashDb:cfg.ConnectionStrings__CashDb.replace(/Database=([^;]+)/i,`Database=${testDb}`),ASPNETCORE_URLS:apiBase,ASPNETCORE_ENVIRONMENT:'Test',Seed__AdminLogin:'e2e-admin',Seed__AdminPassword:e2eAdminPassword,Jwt__Key:crypto.randomBytes(48).toString('hex'),Jwt__Issuer:'FaizaCash',Jwt__Audience:'FaizaCash',Logs__Directory:'.runtime/e2e-api-logs'},stdio:['ignore',fs.openSync('.runtime/e2e-api.log','w'),'inherit']});
  await waitFor(apiBase+'/health');
  const initial=await request('/auth/login',{method:'POST',auth:null,body:{login:'e2e-admin',password:e2eAdminPassword}});token=initial.accessToken;
  await request('/auth/change-password',{method:'POST',body:{oldPassword:e2eAdminPassword,newPassword:'AdminE2E2026A'}});
  const currencies=await request('/currencies');
  const box=await request('/cashboxes',{method:'POST',body:{code:'E2E',name:'E2E cashbox',currencyId:currencies[0].id,balanceLimit:null,branchId:1}});
  await request('/categories',{method:'POST',body:{name:'E2E income',type:'income',parentId:null}});await request('/categories',{method:'POST',body:{name:'E2E expense',type:'expense',parentId:null}});
  const categories=await request('/categories');
  const cashierPassword='CashierE2E2026A',accountantPassword='AccountantE2E2026A';
  const cashier=await request('/users',{method:'POST',body:{login:'e2e-cashier',fullName:'E2E Cashier',password:cashierPassword,roles:['cashier'],mustChangePassword:false}});
  const accountant=await request('/users',{method:'POST',body:{login:'e2e-accountant',fullName:'E2E Accountant',password:accountantPassword,roles:['accountant'],mustChangePassword:false}});
  await request('/access',{method:'POST',body:{userId:cashier.id,cashboxId:box.id}});await request('/access',{method:'POST',body:{userId:accountant.id,cashboxId:box.id}});
  webServer=spawn(process.execPath,['frontend/node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','5174'],{cwd:'frontend',env:{...process.env,API_PROXY:apiBase},stdio:['ignore',fs.openSync('.runtime/e2e-vite.log','w'),'inherit']});
  await waitFor(webBase);
  browser=await chromium.launch({headless:true});
  await test('browser rejects incorrect credentials',async()=>{const page=await browser.newPage();await page.goto(webBase);await page.locator('input[name="login"]').fill('e2e-cashier');await page.locator('input[name="password"]').fill('wrong-password');await page.locator('form').filter({has:page.locator('input[name="login"]')}).getByRole('button').click();await page.getByRole('alert').waitFor();await page.close();});
  await test('cashier opens a shift, posts income and expense, then closes it',async()=>{const page=await browser.newPage({viewport:{width:1280,height:800}});await login(page,'e2e-cashier',cashierPassword);await page.locator('a[href="#/shifts"]').click();await page.getByLabel(/Начальный остаток/).fill('0');await page.getByRole('button',{name:'Открыть смену'}).click();await page.getByText('Смена открыта').waitFor();
    async function post(kind,amount,category){await page.locator('a[href="#/overview"]').click();await page.getByRole('button',{name:kind==='income'?'Новый приход':'Новый расход'}).click();await page.locator('#field-amount').fill(amount);await page.selectOption('#field-categoryId',{label:category});await page.locator('#field-description').fill(`E2E ${kind}`);await page.getByRole('button',{name:'Создать черновик'}).click();await page.getByRole('button',{name:'Провести'}).click();await page.getByRole('button',{name:'Подтвердить проведение'}).click();await page.getByText('Документ проведён. Остаток обновлён.').waitFor();}
    await post('income','1000.00','E2E income');await post('expense','250.00','E2E expense');await page.locator('a[href="#/shifts"]').click();await page.getByLabel(/Фактический остаток/).fill('750');await page.getByRole('button',{name:'Закрыть смену'}).click();await page.getByText('Смена закрыта').waitFor();await page.screenshot({path:'.runtime/e2e-cashier-closed-shift.png',fullPage:true});await page.close();});
  await test('accountant finds the closed day and exports a report through the UI',async()=>{const page=await browser.newPage({viewport:{width:390,height:844}});await login(page,'e2e-accountant',accountantPassword);await page.locator('a[href="#/reports"]').click();await page.getByText('Кассовая книга').waitFor();const download=page.waitForEvent('download');await page.getByRole('button',{name:/CSV/}).click();await (await download).cancel();await page.screenshot({path:'.runtime/e2e-accountant-mobile.png',fullPage:true});await page.close();});
} finally {
  if(browser)await browser.close();
  for(const child of [webServer,apiServer])if(child&&!child.killed)child.kill();
  fs.writeFileSync('.runtime/e2e-results.json',JSON.stringify({time:new Date().toISOString(),database:'dedicated loopback _test database',results},null,2));
  console.log(`E2E checks: ${results.filter(x=>x.status==='passed').length} passed, ${results.filter(x=>x.status!=='passed').length} failed.`);
  if(results.some(x=>x.status!=='passed'))process.exitCode=1;
}
