import {spawn} from 'node:child_process';
// Database suites share one throwaway schema and must run sequentially.
const npm=process.platform==='win32'?'npm.cmd':'npm';
const commands=[
 ['dotnet',['build','backend/FaizaCash.sln','--nologo']],
 ['dotnet',['test','backend/tests/FaizaCash.UnitTests','--no-build','--nologo','--logger','trx;LogFileName=unit-tests.trx','--results-directory','.runtime/unit-results']],
 [npm,['--prefix','frontend','test']],
 [npm,['--prefix','frontend','run','build']],
 [process.execPath,['tests/integration.mjs','--suite=integration']],
 [process.execPath,['tests/security.mjs']],
 [process.execPath,['tests/catalog.mjs']],
 [process.execPath,['tests/ledger-integrity.mjs']],
 [process.execPath,['tests/integration/p0.mjs']],
 [process.execPath,['tests/integration/invariants.mjs']],
 [process.execPath,['tests/integration/restore.mjs']],
 [process.execPath,['tests/integration/p1.mjs']],
 [process.execPath,['tests/integration/demo-seed.mjs']],
 [process.execPath,['tests/integration/p2.mjs']],
 [process.execPath,['tests/integration/wave1.mjs']],
 [process.execPath,['tests/e2e.mjs']]
];
// Continue after a failing suite so one run reports every result; exit non-zero if any failed.
const failures=[];
for(const [command,args] of commands){
 const code=await new Promise((resolve,reject)=>{const child=spawn(command,args,{stdio:'inherit',shell:command.endsWith('.cmd')});child.on('error',reject);child.on('exit',resolve);});
 if(code!==0){failures.push(args.join(' '));process.exitCode=typeof code==='number'&&code!==0?code:1;if(command==='dotnet'&&args[0]==='build')break;}
}
console.log(failures.length?`FAILED suites: ${failures.join(' | ')}`:'All suites passed.');
