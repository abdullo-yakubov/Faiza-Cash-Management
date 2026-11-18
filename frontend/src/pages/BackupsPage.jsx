import React from 'react';
import {api} from '../services/api.js';

export function BackupsPage({data,onRefresh,busy,setBusy,setNotice,setError}) {
  const [status,setStatus]=React.useState(null);
  const [statusError,setStatusError]=React.useState('');
  React.useEffect(()=>{const controller=new AbortController();api('/backups/status',{signal:controller.signal}).then(value=>{setStatus(value);setStatusError('');}).catch(error=>{if(!controller.signal.aborted)setStatusError(error.message);});return()=>controller.abort();},[data]);
  const run=async(action,message)=>{setBusy(true);setError('');try{await action();setNotice(message);onRefresh();}catch(e){setError(e.message);}finally{setBusy(false);}};
  return <section className="panel">
    {statusError&&<div className="error" role="alert">Не удалось загрузить статус резервного копирования: {statusError}</div>}
    {status&&<p className="field-hint">Последняя попытка: {status.lastAttemptAt?new Date(status.lastAttemptAt).toLocaleString('ru-RU'):'ещё не было'}. Успех: {status.lastSuccessfulAt?new Date(status.lastSuccessfulAt).toLocaleString('ru-RU'):'нет'}. Статус: {status.status}. Интервал: {status.intervalHours} ч. Хранение: {status.retentionDays} дн.{status.error?` Ошибка: ${status.error}`:''}</p>}
    <div className="panel-heading"><div><h2>Резервные копии PostgreSQL</h2><p>Файлы хранятся на сервере. Клиентские пути не принимаются.</p></div><button className="button primary" disabled={busy} onClick={()=>run(()=>api('/backups',{method:'POST'}),'Резервная копия создана.')}>Создать копию</button></div>
    <div className="table-scroll"><table><thead><tr><th>Идентификатор</th><th>Дата</th><th>Размер</th><th>SHA-256</th><th>Статус</th><th>Проверка</th></tr></thead><tbody>{(data||[]).map(item=><tr key={item.id}><td><strong>{item.id}</strong></td><td>{new Date(item.createdAt).toLocaleString('ru-RU')}</td><td>{(item.sizeBytes/1024/1024).toFixed(2)} MB</td><td><code>{item.sha256}</code></td><td><span className="badge posted">{item.status}</span></td><td><button className="text-btn" disabled={busy} onClick={()=>run(()=>api('/backups/'+encodeURIComponent(item.id)+'/verify',{method:'POST'}),'Целостность подтверждена.')}>Проверить</button></td></tr>)}</tbody></table>{!data?.length&&<p className="muted">Резервных копий пока нет.</p>}</div>
    <p className="field-hint">Восстановление отключено, пока администратор не настроит отдельную изолированную тестовую базу и необходимые права. Рабочая база не будет перезаписана.</p>
  </section>;
}
