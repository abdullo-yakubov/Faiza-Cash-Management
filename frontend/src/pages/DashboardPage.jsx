import React,{useEffect,useState} from 'react';
import {ResponsiveContainer,BarChart,Bar,LineChart,Line,XAxis,YAxis,Tooltip,CartesianGrid,Legend} from 'recharts';
import {routeHash} from '../utils/route.js';
import {api} from '../services/api.js';
import {ManagerOverview} from '../components/ManagerOverview.jsx';
import {SkeletonBlock, ErrorState} from '../components/ui';
import {listTotal} from '../services/endpoints.js';

const money=(value,c='TJS')=>{
  const raw=String(value??0),negative=raw.startsWith('-'),[whole='0',fraction='']=raw.replace(/^-/, '').split('.');
  const grouped=whole.replace(/\B(?=(\d{3})+(?!\d))/g,'\u202f');
  return `${negative?'-':''}${grouped},${(fraction+'00').slice(0,2)} ${c}`;
};
const exceeds=(left,right)=>{
  const cents=value=>{
    const raw=String(value??0),negative=raw.startsWith('-'),[whole='0',fraction='']=raw.replace(/^-/, '').split('.');
    return (negative?-1n:1n)*(BigInt(whole||'0')*100n+BigInt((fraction+'00').slice(0,2)));
  };
  return cents(left)>cents(right);
};
const iso=value=>{
  const parts=Object.fromEntries(new Intl.DateTimeFormat('en-US',{timeZone:'Asia/Dushanbe',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(value)).map(x=>[x.type,x.value]));
  return `${parts.year}-${parts.month}-${parts.day}`;
};
const presets={
  today:()=>({from:iso(Date.now()),to:iso(Date.now())}),
  yesterday:()=>({from:iso(Date.now()-86400000),to:iso(Date.now()-86400000)}),
  week:()=>({from:iso(Date.now()-6*86400000),to:iso(Date.now())}),
  month30:()=>({from:iso(Date.now()-29*86400000),to:iso(Date.now())}),
  month:()=>({from:iso(Date.now()).slice(0,8)+'01',to:iso(Date.now())})
};
function Loading(){return <SkeletonBlock tiles={7} rows={3}/>}
// FG7-007: severity is conveyed by text and colour together; colours come from tokens.css.
const severityLabel={danger:'Критично',warning:'Внимание',info:'Сведения'};
const debtStatusLabel={open:'Открыта',partially_paid:'Частично оплачена',paid:'Оплачена'};

export function DashboardPage({onDocument,currency='TJS',boxes=[],categories=[],parties=[]}){
  const [result,setResult]=useState(null);
  const [reduceMotion,setReduceMotion]=useState(()=>window.matchMedia?.('(prefers-reduced-motion: reduce)').matches??false);
  const [f,setF]=useState({from:presets.month30().from,to:presets.month30().to,branchId:'',cashboxId:'',categoryId:'',counterpartyId:''});
  const query=new URLSearchParams(Object.entries(f).filter(([,value])=>value)).toString();
  const current=result?.query===query?result:null;
  const raw=current?.data??{};
  // Пункты волны 1 в «Требует внимания»: переход в отфильтрованный список одним кликом.
  const d={...raw,attention:[
    ...(raw.pendingApprovals?[{code:'pending_approvals',severity:'warning',title:'Расходы ждут согласования',message:'Документов в очереди: '+raw.pendingApprovals+'. Без согласования их нельзя провести.',href:'#/approvals'}]:[]),
    ...(raw.overdueAdvances?[{code:'overdue_advances',severity:'danger',title:'Просроченные авансы',message:'Подотчётных лиц с просроченным отчётом: '+raw.overdueAdvances+'.',href:'#/advances?filter=overdue'}]:[]),
    ...(raw.attention||[])]};
  const [attempt,setAttempt]=useState(0);
  useEffect(()=>{
    const preference=window.matchMedia?.('(prefers-reduced-motion: reduce)');
    if(!preference)return;
    const update=()=>setReduceMotion(preference.matches);
    preference.addEventListener?.('change',update);
    return()=>preference.removeEventListener?.('change',update);
  },[]);
  const [drafts,setDrafts]=useState(null);
  // Черновиков нет в ответе /dashboard: суммируем total по доступным кассам (запрос pageSize=1).
  useEffect(()=>{
    const controller=new AbortController();
    const targets=boxes.filter(b=>(!f.cashboxId||String(b.id)===f.cashboxId)&&(!f.branchId||String(b.branchId)===f.branchId));
    if(!targets.length){setDrafts(0);return;}
    Promise.all(targets.map(b=>api('/operations?'+new URLSearchParams({cashboxId:b.id,from:f.from,to:f.to,status:'draft',page:1,pageSize:1}),{signal:controller.signal}).then(listTotal)))
      .then(values=>{if(!controller.signal.aborted)setDrafts(values.reduce((a,b)=>a+b,0));}).catch(()=>{if(!controller.signal.aborted)setDrafts(null);});
    return()=>controller.abort();
  },[boxes,f.cashboxId,f.branchId,f.from,f.to]);
  useEffect(()=>{
    const controller=new AbortController();
    api('/dashboard?'+query,{signal:controller.signal})
      .then(data=>{if(!controller.signal.aborted)setResult({query,data});})
      .catch(error=>{if(!controller.signal.aborted)setResult({query,error:error.message});});
    return()=>controller.abort();
  },[query,attempt]);
  const series=(d.cashflow||[]).map(x=>({...x,income:Number(x.income),expense:Number(x.expense),net:Number(x.net)}));
  const balanceSeries=(d.balanceTrend||[]).map(x=>({...x,balance:Number(x.balance)}));
  const periods=[
    ['Сегодня','todayIncome','todayExpense'],
    ['7 дней','weekIncome','weekExpense'],
    ['Месяц','monthIncome','monthExpense']
  ];
  const kpis=[
    ['Остаток',d.totalBalance,'money'],
    ['Приход',d.income,'money'],
    ['Расход',d.expense,'money'],
    ...(d.canViewDebts?[['Просроченный долг',d.overdue,'money']]:[]),
    ['Чистый поток',d.net,'money'],
    ['Операции',d.operationCount,'num'],
    ['Активные кассы',d.activeCashboxes,'num'],
    ['Открытые смены',d.openShifts?.length||0,'num'],
    ['Переводы',d.transfers,'money']
  ];
  return <section className="v5-page dashboard-view">
    <div className="v5-toolbar">
      <div className="quick-periods">
        {[['today','Сегодня'],['yesterday','Вчера'],['week','7 дней'],['month30','30 дней'],['month','Месяц']].map(([id,label])=><button key={id} type="button" onClick={()=>setF({...f,...presets[id]()})}>{label}</button>)}
      </div>
      <label>С <input type="date" value={f.from} onChange={e=>setF({...f,from:e.target.value})}/></label>
      <label>По <input type="date" value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label>
      <select aria-label="Филиал" value={f.branchId} onChange={e=>setF({...f,branchId:e.target.value,cashboxId:''})}><option value="">Все филиалы</option>{[...new Map(boxes.map(x=>[x.branchId,x])).values()].map(x=><option key={x.branchId} value={x.branchId}>{x.branch}</option>)}</select>
      <select aria-label="Касса" value={f.cashboxId} onChange={e=>setF({...f,cashboxId:e.target.value})}><option value="">Все кассы</option>{boxes.filter(x=>!f.branchId||String(x.branchId)===f.branchId).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select>
      <details className="dashboard-extra"><summary>Дополнительные фильтры{f.categoryId||f.counterpartyId?' · активны':''}</summary>
      <select aria-label="Категория" value={f.categoryId} onChange={e=>setF({...f,categoryId:e.target.value})}><option value="">Все категории</option>{categories.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select>
      <select aria-label="Контрагент" value={f.counterpartyId} onChange={e=>setF({...f,counterpartyId:e.target.value})}><option value="">Все контрагенты</option>{parties.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select>
      </details>
    </div>
    {current?.error?<section className="panel"><ErrorState error={current.error} onRetry={()=>{setResult(null);setAttempt(v=>v+1);}}/></section>:!current?<Loading/>:<>
    <ManagerOverview d={d} currency={currency} draftCount={drafts} reduceMotion={reduceMotion}/>
    <p className="muted">Остаток на {d.period?.to||f.to}</p>
    <p className="muted">Приход и расход не включают межкассовые переводы. Переводы показаны отдельным показателем. Суммы одной валюты учёта.</p>
    <h2 className="section-title">Показатели выбранного периода</h2>
    <div className="v5-kpis dashboard-metrics">{kpis.map(([label,value,kind])=>{
      const overdue = label==='Просроченный долг' && exceeds(value,0);
      return <div className={overdue?'stat metric-overdue':'stat'} key={label}>
        <span className="stat-label">{label}</span>
        <strong className="stat-value" key={String(value)}>{kind==='money'?money(value,currency):value}</strong>
      </div>;
    })}</div>
    <section className="panel dashboard-attention" aria-labelledby="dashboard-attention-title">
      <h2 id="dashboard-attention-title">Требует внимания</h2>
      {d.attention?.length?<ul className="attention-list">{d.attention.map((item,index)=><li key={item.code+':'+(item.objectId??index)} className={'attention-item severity-'+item.severity}>
        <span className="attention-level">{severityLabel[item.severity]||'Внимание'}</span>
        <div><strong>{item.title}</strong><p>{item.message}</p></div>
        {item.amount!=null&&<strong className="attention-amount">{money(item.amount,currency)}</strong>}
        <a href={item.code==='cash_limit'?routeHash({page:'operations',box:item.objectId,from:f.from,to:f.to,status:'posted'}):item.href}>Открыть</a>
      </li>)}</ul>:<p className="muted attention-ok"><span className="attention-level">Норма</span> В выбранных кассах и периоде отклонений не обнаружено.</p>}
    </section>
    <section className="panel"><div className="panel-heading"><h2>Приход и расход за периоды</h2></div><div className="v5-kpis">{periods.map(([label,income,expense])=><div className="stat" key={label}><span className="stat-label">{label}</span><strong className="stat-value">+ {money(d.periodTotals?.[income],currency)}</strong><span className="stat-label">− {money(d.periodTotals?.[expense],currency)}</span></div>)}</div></section>
    <div className="chart-grid">
      <section className="panel chart-panel"><div className="panel-heading"><div><h2>Приход, расход и поток</h2><p>Внешний оборот без межкассовых переводов</p></div></div><div className="chart">{series.length?<ResponsiveContainer width="100%" height="100%"><BarChart data={series}><CartesianGrid vertical={false} stroke="var(--border)"/><XAxis dataKey="date" tick={{fontSize:11,fill:'var(--muted)'}}/><YAxis tick={{fontSize:11,fill:'var(--muted)'}}/><Tooltip contentStyle={{background:'var(--panel)',borderColor:'var(--border)'}}/><Legend/><Bar dataKey="income" name="Приход" fill="var(--chart-income)" isAnimationActive={!reduceMotion}/><Bar dataKey="expense" name="Расход" fill="var(--chart-expense)" isAnimationActive={!reduceMotion}/><Bar dataKey="net" name="Поток" fill="var(--chart-neutral)" isAnimationActive={!reduceMotion}/></BarChart></ResponsiveContainer>:<p className="empty">Нет внешнего оборота за период.</p>}</div></section>
      <section className="panel"><div className="panel-heading"><h2>Расход по категориям</h2></div><div className="category-distribution">{(d.categories||[]).filter(x=>x.type==='expense').map(x=><div className="category-line" key={x.name}><div><span>{x.name}</span><strong>{money(x.amount,currency)}</strong></div></div>)}{!(d.categories||[]).some(x=>x.type==='expense')&&<p className="muted">Нет расходов.</p>}</div></section>
    </div>
    <section className="panel chart-panel"><div className="panel-heading"><div><h2>Динамика денежного остатка</h2><p>По выбранным доступным кассам, с учётом переводов</p></div></div><div className="chart">{balanceSeries.length?<ResponsiveContainer width="100%" height="100%"><LineChart data={balanceSeries}><CartesianGrid vertical={false} stroke="var(--border)"/><XAxis dataKey="date" tick={{fontSize:11,fill:'var(--muted)'}}/><YAxis tick={{fontSize:11,fill:'var(--muted)'}}/><Tooltip contentStyle={{background:'var(--panel)',borderColor:'var(--border)'}} formatter={value=>money(value,currency)}/><Line type="monotone" dataKey="balance" name="Остаток" stroke="var(--chart-neutral)" strokeWidth={2} dot={false} isAnimationActive={!reduceMotion}/></LineChart></ResponsiveContainer>:<p className="empty">Нет данных за период.</p>}</div></section>
    <h3>Сравнение касс и филиалов</h3>
    <div className="v5-cards">{(d.balances||[]).map(x=><article className="v5-card" key={x.id}><small>{x.branch}</small><h3>{x.name}</h3><strong>{money(x.balance,currency)}</strong>{x.limit!=null&&exceeds(x.balance,x.limit)&&<span className="badge draft">Лимит превышен</span>}</article>)}</div>
    <div className="v5-two"><div className="v5-note"><h3>Филиалы</h3>{(d.branches||[]).map(x=><div className="v5-history" key={x.id}>{x.name} · {money(x.balance,currency)}</div>)}</div>{d.canViewDebts&&<div className="v5-note"><h3>Задолженности</h3>{(d.debtStatuses||[]).map(x=><div className="v5-history" key={x.status}>{debtStatusLabel[x.status]||x.status} · {x.count} · {money(x.total,currency)}</div>)}{(d.debts||[]).map(x=><div className="v5-history" key={x.direction}>{x.direction==='receivable'?'К получению':'К оплате'} · {money(x.total,currency)}</div>)}</div>}</div>
    <section className="panel"><div className="panel-heading"><h2>Последние операции</h2></div>{(d.latestOperations||[]).map(x=><div className="v5-history" key={x.id}><span>{x.date} · {d.balances?.find(b=>b.id===x.cashboxId)?.name||'Касса'} · {x.transfer?'Перевод':x.type==='income'?'Приход':'Расход'} · {x.description} · {x.status==='posted'?'Проведена':'Черновик'}</span><strong>{money(x.amount,currency)}</strong><button className="button secondary" type="button" onClick={()=>onDocument(x)}>Открыть документ № {x.id}</button></div>)}{!d.latestOperations?.length&&<p className="muted">Операций пока нет.</p>}</section>
    </>}
  </section>;
}
