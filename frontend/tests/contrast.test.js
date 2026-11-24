import test from 'node:test';import assert from 'node:assert/strict';import fs from 'node:fs';
function luminance(hex){let value=hex.replace('#','');if(value.length===3)value=[...value].map(c=>c+c).join('');const rgb=value.match(/.{2}/g).map(v=>parseInt(v,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return .2126*rgb[0]+.7152*rgb[1]+.0722*rgb[2];}
function ratio(a,b){return(Math.max(luminance(a),luminance(b))+.05)/(Math.min(luminance(a),luminance(b))+.05);}
const read=path=>fs.readFileSync(new URL(path,import.meta.url),'utf8');
// Все блоки с данным селектором сливаются (поздние значения переопределяют ранние), var() не раскрывается.
function themeTokens(css,selector){
  const tokens={};
  for(const match of css.replace(/\/\*[\s\S]*?\*\//g,'').matchAll(/([^{}]+)\{([^}]*)\}/g)){
    if(match[1].trim()!==selector)continue;
    for(const t of match[2].matchAll(/--([\w-]+):\s*(#[\da-f]{3,6})\b/gi))tokens[t[1]]=t[2];
  }
  return tokens;
}

test('primary and secondary text design tokens satisfy AA contrast in both themes',()=>{
  const css=read('../src/styles/refinements.css');
  for(const block of [css.match(/:root\{([^}]+)/)[1],css.match(/\[data-theme=dark\]\{([^}]+)/)[1]]){
    const tokens=Object.fromEntries([...block.matchAll(/--([\w-]+):(#[\da-f]{3,6})/gi)].map(m=>[m[1],m[2]]));
    for(const text of ['text','muted'])for(const background of ['panel','bg'])assert.ok(ratio(tokens[text],tokens[background])>=4.5,`${text} on ${background} must meet 4.5:1`);
  }
});

test('семантические токены tokens.css проходят WCAG AA в светлой и тёмной темах',()=>{
  const css=read('../src/styles/tokens.css');
  for(const theme of ['light','dark']){
    const t=themeTokens(css,`[data-theme="${theme}"]`);
    const pairs=[
      ...['text','muted','income','expense','accent'].flatMap(fg=>['bg','panel','panel-raised'].map(bg=>[fg,bg])),
      ['text','input'],['income','income-bg'],['expense','expense-bg'],['accent-contrast','accent'],['text','accent-soft'],
      ...['danger','warning','success','info'].map(s=>[`status-${s}-fg`,`status-${s}-bg`])
    ];
    for(const [fg,bg] of pairs){
      assert.ok(t[fg]&&t[bg],`${theme}: нет токена ${fg} или ${bg}`);
      const value=ratio(t[fg],t[bg]);
      assert.ok(value>=4.5,`${theme}: ${fg} на ${bg} = ${value.toFixed(2)}:1, нужно ≥ 4.5:1`);
    }
    // Нетекстовые элементы (графики, рамка фокуса) — ≥ 3:1 по WCAG 1.4.11.
    for(const fg of ['chart-income','chart-expense','focus-ring'])assert.ok(ratio(t[fg],t.panel)>=3,`${theme}: ${fg} на panel ниже 3:1`);
  }
});
