import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';

const bin = process.env.PG_BIN || 'C:/Program Files/PostgreSQL/18/bin';
const config = JSON.parse(fs.readFileSync('.runtime/local.json','utf8'));
const fields = Object.fromEntries(config.ConnectionStrings__CashDb.split(';').map(pair=>pair.split('=')));
assert.equal(fields.Host,'127.0.0.1');
assert.match(fields.Database,/^faiza_[a-z0-9]+$/);
const source = fields.Database+'_test';
const dir = fs.mkdtempSync(path.resolve('.runtime/restore-'));
const password = crypto.randomBytes(32).toString('hex');
const secret = path.join(dir,'init-password');
const archive = path.join(dir,'source.dump');
const cluster = path.join(dir,'cluster');
const port = String(crypto.randomInt(50000,60000));
const origin = ['-h','127.0.0.1','-p',fields.Port,'-U',fields.Username,'-d',source];
const target = ['-h','127.0.0.1','-p',port,'-U','restore_admin','-d','postgres'];
function run(tool,args,pw,input) {
  const result=spawnSync(path.join(bin,tool),args,{encoding:'utf8',input,env:{...process.env,PGPASSWORD:pw,PGCLIENTENCODING:'UTF8'},maxBuffer:16*1024*1024});
  if(result.status!==0) throw new Error(tool+': '+(result.error?.message||result.stderr));
  return result.stdout.trim();
}
function signature(connection,pw) {
  const sql=q=>run('psql',[...connection,'-XAt','-v','ON_ERROR_STOP=1'],pw,q);
  const tables=sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;").split(/\r?\n/);
  assert.ok(tables.includes('Operations') && tables.includes('__EFMigrationsHistory'));
  return tables.map(name=>{
    const identifier='"'+name.replaceAll('"','""')+'"';
    return [name,sql("SELECT count(*) || ':' || md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(to_jsonb(t)::text) h FROM public."+identifier+" t) rows;")];
  });
}
let started=false;
try {
  fs.writeFileSync(secret,password);
  run('initdb',['-D',cluster,'-U','restore_admin','--auth=scram-sha-256','--pwfile='+secret,'--encoding=UTF8','--locale=C'],password);
  fs.unlinkSync(secret);
  run('pg_ctl',['-D',cluster,'-l',path.join(dir,'postgres.log'),'-o','-h 127.0.0.1 -p '+port,'-w','start'],password);
  started=true;
  const before=signature(origin,fields.Password);
  run('pg_dump',[...origin,'-Fc','-f',archive],fields.Password);
  run('pg_restore',[...target,'--single-transaction','--no-owner','--no-privileges',archive],password);
  assert.deepEqual(signature(target,password),before);
  assert.throws(()=>run('pg_restore',[...target,'--single-transaction','--no-owner','--no-privileges',archive],password));
  assert.deepEqual(signature(target,password),before);
  fs.writeFileSync(path.join(dir,'result.json'),JSON.stringify({passed:true,tables:before.length,duplicateRestoreRolledBack:true}));
  console.log('PASS isolated restore: '+before.length+' tables matched; repeated restore rolled back.');
} finally {
  if(fs.existsSync(secret))fs.unlinkSync(secret);
  if(started)run('pg_ctl',['-D',cluster,'-w','stop'],password);
}
