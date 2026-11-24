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

// FG7-024: synthetic three-branch network. Branch ids 1..3 are seeded by the Branches migration
// (Душанбе, Худжанд, Бохтар). All names are fictional and marked «демо».
export const demoCashboxes=[
  {code:'DSH-MAIN',name:'Основная касса · Душанбе',branchId:1,opening:'25000.00',balanceLimit:null},
  {code:'KHJ-01',name:'Касса филиала · Худжанд',branchId:2,opening:'8000.00',balanceLimit:'20000.00'},
  {code:'BKH-01',name:'Касса филиала · Бохтар',branchId:3,opening:'5000.00',balanceLimit:'15000.00'}
];
// Operations for the branch cashboxes, by day offset from today (negative = past).
export const demoBranchOperations=[
  {box:'KHJ-01',day:-12,type:'income',category:'Выручка от реализации',party:0,amount:'3200.00',description:'Выручка филиала Худжанд (демо)'},
  {box:'KHJ-01',day:-12,type:'expense',category:'Хозяйственные расходы',party:1,amount:'640.00',description:'Канцтовары для филиала (демо)'},
  {box:'KHJ-01',day:-6,type:'income',category:'Выручка от реализации',party:0,amount:'2750.50',description:'Выручка филиала Худжанд (демо)'},
  {box:'BKH-01',day:-9,type:'income',category:'Выручка от реализации',party:0,amount:'1900.00',description:'Выручка филиала Бохтар (демо)'},
  {box:'BKH-01',day:-9,type:'expense',category:'Подотчётные суммы',party:2,amount:'400.00',description:'Выдача под отчёт (демо)'},
  {box:'BKH-01',day:-4,type:'income',category:'Выручка от реализации',party:0,amount:'1350.00',description:'Выручка филиала Бохтар (демо)'}
];
export const demoTransfer={day:-3,from:'DSH-MAIN',to:'KHJ-01',amount:'2000.00',description:'Подкрепление кассы филиала Худжанд (демо)'};
// One historical shift is closed with a cash shortage so the Dashboard "attention" block has data.
export const demoDiscrepancy={box:'BKH-01',day:-9,difference:'-15.00'};
export const demoDebts=[
  {party:0,box:'DSH-MAIN',direction:'receivable',basisType:'order',basisReference:'ЗАКАЗ-1042 (демо)',totalAmount:'6000.00',dueOffset:10,payment:'2500.00',representative:true},
  {party:1,box:'DSH-MAIN',direction:'payable',basisType:'delivery',basisReference:'ПОСТАВКА-77 (демо)',totalAmount:'3000.00',dueOffset:-5,payment:null,representative:false},
  {party:0,box:'KHJ-01',direction:'receivable',basisType:'order',basisReference:'ЗАКАЗ-1050 (демо)',totalAmount:'1200.00',dueOffset:-1,payment:null,representative:true}
];

function moneyFromCents(cents){
  return `${cents/100n}.${String(cents%100n).padStart(2,'0')}`;
}
