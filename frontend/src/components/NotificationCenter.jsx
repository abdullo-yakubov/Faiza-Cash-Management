import React from 'react';
import {Bell,Check,ChevronRight} from 'lucide-react';
import {api} from '../services/api.js';

export function NotificationCenter(){
  const [open,setOpen]=React.useState(false),[state,setState]=React.useState({items:[],loading:true,error:''});
  const load=React.useCallback(async signal=>{
    try{const items=await api('/notifications',{signal});setState({items,loading:false,error:''});}
    catch(error){if(error.name!=='AbortError')setState(previous=>({...previous,loading:false,error:error.message}));}
  },[]);
  React.useEffect(()=>{const controller=new AbortController();load(controller.signal);return()=>controller.abort();},[load]);
  const unread=state.items.filter(item=>!item.isRead).length;
  async function markRead(item){
    if(item.isRead)return;
    setState(current=>({...current,items:current.items.map(value=>value.key===item.key?{...value,isRead:true}:value)}));
    try{await api('/notifications/'+encodeURIComponent(item.key)+'/read',{method:'POST'});}
    catch(error){setState(current=>({...current,items:current.items.map(value=>value.key===item.key?{...value,isRead:false}:value),error:error.message}));}
  }
  return <div className="notification-center">
    <button className="icon-btn notification-trigger" type="button" aria-label={unread?`Уведомления: ${unread} непрочитанных`:'Уведомления'} aria-expanded={open} onClick={()=>setOpen(value=>!value)}><Bell size={20}/>{unread>0&&<span className="notification-count">{unread}</span>}</button>
    {open&&<section className="notification-popover" aria-label="Уведомления">
      <header><div><h2>Уведомления</h2><p>{unread?`Непрочитанных: ${unread}`:'Все просмотрены'}</p></div><button className="text-btn" type="button" onClick={()=>load()}>Обновить</button></header>
      {state.error&&<p className="notification-error" role="alert">{state.error}</p>}
      {state.loading?<p className="muted">Загрузка…</p>:!state.items.length?<p className="muted">Новых уведомлений нет.</p>:<ul>{state.items.map(item=><li key={item.key} className={item.isRead?'is-read':`priority-${item.priority}`}>
        <a href={item.actionHref} onClick={()=>{markRead(item);setOpen(false);}}><span><strong>{item.reason}</strong><small>{item.date} · {item.objectType}</small></span><ChevronRight size={16}/></a>
        {!item.isRead&&<button type="button" className="text-btn" onClick={()=>markRead(item)}><Check size={15}/>Прочитано</button>}
      </li>)}</ul>}
    </section>}
  </div>;
}
