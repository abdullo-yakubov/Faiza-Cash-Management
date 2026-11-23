import {api,setToken} from '../frontend/src/services/api.js';
import fs from 'node:fs';
import crypto from 'node:crypto';

const configPath='.runtime/local.json';
const config=fs.existsSync(configPath)?JSON.parse(fs.readFileSync(configPath,'utf8')):{};
const adminLogin=process.env.FAIZA_ADMIN_LOGIN||config.Seed__AdminLogin||'admin';
const adminPassword=process.env.FAIZA_ADMIN_PASSWORD||config.Seed__AdminPassword;
if(!adminPassword)throw new Error('Задайте FAIZA_ADMIN_PASSWORD для локального администратора.');

const base=process.env.FAIZA_API||'http://localhost:5080';
const nativeFetch=globalThis.fetch;
globalThis.fetch=(url,options)=>nativeFetch(new URL(url,base),options);
const login=await api('/auth/login',{method:'POST',body:{login:adminLogin,password:adminPassword}});
setToken(login.accessToken);
if(login.user.mustChangePassword)throw new Error('Сначала войдите в интерфейс и смените первоначальный пароль администратора.');

const boxes=await api('/cashboxes');
if(!boxes.length)throw new Error('Сначала создайте кассу и наполните учебную базу.');
const response=await api('/users?page=1&pageSize=500');
const existing=response.items||response;
const credentialsPath='.runtime/demo-users.json';
const credentials=fs.existsSync(credentialsPath)?JSON.parse(fs.readFileSync(credentialsPath,'utf8')):{};
const roles=[['accountant','Демо бухгалтер'],['cashier','Демо кассир'],['manager','Демо руководитель'],['auditor','Демо аудитор']];
const created=[];

for(const [role,fullName] of roles){
  const userLogin='demo_'+role;
  const found=existing.find(user=>user.login===userLogin);
  let user=found;
  if(found&&!credentials[userLogin])throw new Error(`Учётная запись ${userLogin} уже существует, а пароль для неё не сохранён. Сбросьте пароль через интерфейс администратора.`);
  if(!found){
    const password=crypto.randomBytes(18).toString('base64url')+'Aa1';
    user=await api('/users',{method:'POST',body:{login:userLogin,password,fullName,roles:[role],mustChangePassword:true}});
    credentials[userLogin]=password;
    fs.writeFileSync(credentialsPath,JSON.stringify(credentials,null,2),{mode:0o600});
    existing.push(user);
    created.push(userLogin);
  }
  if(!await api('/access').then(rows=>rows.some(row=>row.userId===user.id&&row.cashboxId===boxes[0].id)))
    await api('/access',{method:'POST',body:{userId:user.id,cashboxId:boxes[0].id}});
}

console.log(`Демо-роли доступны для кассы «${boxes[0].name}». Локальные пароли сохранены в ${credentialsPath}; каждый сотрудник сменит пароль при первом входе.`);
if(!created.length)console.log('Новые пользователи не создавались; существующие назначения кассы проверены.');
