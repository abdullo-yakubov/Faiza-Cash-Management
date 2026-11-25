import {userContract} from './contracts.js';
import {demoResponse, demoAugment, demoEnabled, demoFallbackAllowed, isWave1Route, markDemoUsed} from './demo.js';
let accessToken = null;
let refreshing;
export const setToken = value => { accessToken = value; };
async function refresh() {
  refreshing ??= fetch('/api/auth/refresh', {method:'POST',credentials:'include',headers:{'X-Faiza-Request':'1'}}).then(async r=>{if(!r.ok) {setToken(null); throw new ApiError('Сеанс завершён. Войдите в систему повторно.',401);} const data=await r.json(); const user=userContract(data.user); accessToken=data.accessToken; return user;}).finally(()=>{refreshing=null;});
  return refreshing;
}
export const restoreSession=refresh;
export class ApiError extends Error { constructor(message,status=0,errors={}) {super(message);this.name='ApiError';this.status=status;this.errors=errors;} }
const fallbackMessages={400:'Проверьте введённые данные.',401:'Сеанс завершён. Войдите повторно.',403:'Недостаточно прав для этого действия.',404:'Запись не найдена.',409:'Данные уже изменены. Обновите страницу.',413:'Файл слишком большой.',415:'Недопустимый тип файла.',429:'Слишком много запросов. Повторите позже.'};

// Общий транспорт: JSON-тело или FormData (multipart, Content-Type ставит браузер).
async function request(path,{method='GET',body,form,raw=false,retry=true,signal}={}) {
  // Демо-режим: маршруты волны 1 отвечают моками строго по контракту.
  if(demoEnabled()){const mock=await demoResponse(method,path,body??form,{raw});if(mock!==undefined){markDemoUsed(path);return mock;}}
  let r;
  try {r=await fetch('/api'+path,{method,signal,credentials:'include',headers:{'X-Faiza-Request':'1',...(body?{'Content-Type':'application/json'}:{}),...(accessToken?{Authorization:`Bearer ${accessToken}`}:{})},body:form??(body?JSON.stringify(body):undefined)});}
  catch(error){if(error.name==='AbortError')throw error;throw new ApiError('Сервер недоступен. Проверьте соединение и повторите попытку.');}
  if(r.status===401 && retry && !path.startsWith('/auth/')) { await refresh(); return request(path,{method,body,form,raw,retry:false,signal}); }
  if(!r.ok) {
    // Backend волны 1 ещё не слит: в dev-сборке маршрут контракта, вернувший 404/405, отвечает моком.
    if((r.status===404||r.status===405)&&demoFallbackAllowed()&&isWave1Route(method,path)){const mock=await demoResponse(method,path,body??form,{raw});if(mock!==undefined){markDemoUsed(path);return mock;}}
    const p=await r.json().catch(()=>({})); throw new ApiError(p.detail || fallbackMessages[r.status] || 'Не удалось выполнить запрос. Повторите попытку.',r.status,p.errors);
  }
  const data = raw ? await r.blob() : r.status===204 ? null : await r.json();
  return raw ? data : demoAugment(method,path,data);
}
export function api(path,{method='GET',body,raw=false,retry=true,signal}={}) { return request(path,{method,body,raw,retry,signal}); }
export function upload(path,form,method='POST',{signal}={}) { return request(path,{method,form,signal}); }
export async function download(path,name) { const blob=await api(path,{raw:true}); const url=URL.createObjectURL(blob); const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000); }
/** Blob → object URL для предпросмотра (вызвавший обязан освободить URL). */
export async function blobUrl(path,{signal}={}) { const blob=await api(path,{raw:true,signal}); return {url:URL.createObjectURL(blob),type:blob.type}; }
