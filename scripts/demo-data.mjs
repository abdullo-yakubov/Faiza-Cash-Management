// Только вымышленные данные. Никакого исправления существующих документов.
export const demoCategories=[['Начальный остаток','income'],['Выручка от реализации','income'],['Возврат подотчётных средств','income'],['Хозяйственные расходы','expense'],['Расчёты с поставщиками','expense'],['Подотчётные суммы','expense']];
export const demoParties=[['ООО «Сомон Трейд» (демо)','client'],['ООО «Ориён Снаб» (демо)','supplier'],['Саидов Фарид (демо)','employee']];
export function demoOperations(end){
  const kinds=[
    {type:'income',category:'Выручка от реализации',party:demoParties[0][0],description:'Выручка по договору',amount:'4800.00'},
    {type:'expense',category:'Расчёты с поставщиками',party:demoParties[1][0],description:'Оплата поставки материалов',amount:'1800.00'},
    {type:'expense',category:'Хозяйственные расходы',party:demoParties[1][0],description:'Приобретение хозяйственных принадлежностей',amount:'450.50'},
    {type:'expense',category:'Подотчётные суммы',party:demoParties[2][0],description:'Выдача сотруднику под отчёт',amount:'900.00'},
    {type:'income',category:'Возврат подотчётных средств',party:demoParties[2][0],description:'Возврат неизрасходованных подотчётных средств',amount:'240.25'}
  ];
  const rows=[];
  for(let i=0;i<21;i++){
    const date=new Date(end+'T00:00:00Z');date.setUTCDate(date.getUTCDate()-20+i);
    rows.push(i===0?{date:date.toISOString().slice(0,10),type:'income',category:'Начальный остаток',party:null,description:'Начальный остаток · учебные данные',amount:'25000.00',status:'posted'}:{...kinds[(i-1)%kinds.length],date:date.toISOString().slice(0,10),description:kinds[(i-1)%kinds.length].description+' №'+(100+i)+' (демо)',status:'posted'});
  }
  for(const kind of [kinds[0],kinds[1],kinds[3]])rows.push({...kind,date:end,description:kind.description+' · на проверке (демо)',status:'draft'});
  return rows;
}

export function demoShiftPlan(rows){
  const byDate=new Map();
  for(const row of rows){
    if(!byDate.has(row.date))byDate.set(row.date,[]);
    byDate.get(row.date).push(row);
  }
  let balance=0n;
  const dates=[...byDate.keys()].sort();
  return dates.map((date,index)=>{
    const openingBalance=balance;
    for(const row of byDate.get(date)){
      if(row.status!=='posted')continue;
      const amount=BigInt(row.amount.replace('.',''));
      balance+=row.type==='income'?amount:-amount;
      if(balance<0n)throw new Error(`Demo ledger becomes negative on ${date}`);
    }
    return {date,openingBalance:moneyFromCents(openingBalance),closingBalance:moneyFromCents(balance),close:index<dates.length-1};
  });
}

function moneyFromCents(cents){
  return `${cents/100n}.${String(cents%100n).padStart(2,'0')}`;
}
