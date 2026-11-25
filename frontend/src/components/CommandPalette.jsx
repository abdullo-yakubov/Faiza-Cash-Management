import React, {useEffect, useMemo, useRef, useState} from 'react';
import {Search} from 'lucide-react';

/** Командная палитра Ctrl+K / ⌘K: быстрый переход по разделам, доступным пользователю. */
export function CommandPalette({items, onGo}) {
  const [open, setOpen] = useState(false), [q, setQ] = useState(''), [active, setActive] = useState(0);
  const input = useRef(null), previous = useRef(null);
  useEffect(() => {
    const key = e => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); previous.current = document.activeElement; setOpen(v => !v); setQ(''); setActive(0); }
    };
    window.addEventListener('keydown', key);
    return () => window.removeEventListener('keydown', key);
  }, []);
  useEffect(() => { if (open) input.current?.focus(); else if (previous.current?.isConnected) previous.current.focus(); }, [open]);
  const found = useMemo(() => {
    const text = q.trim().toLowerCase();
    return items.filter(([, label, , group]) => !text || label.toLowerCase().includes(text) || group.toLowerCase().includes(text));
  }, [q, items]);
  if (!open) return null;
  const choose = item => { setOpen(false); onGo(item[0]); };
  function keyDown(e) {
    if (e.key === 'Escape') { e.preventDefault(); setOpen(false); }
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(i => Math.min(found.length - 1, i + 1)); }
    if (e.key === 'ArrowUp') { e.preventDefault(); setActive(i => Math.max(0, i - 1)); }
    if (e.key === 'Enter' && found[active]) { e.preventDefault(); choose(found[active]); }
  }
  return <div className="cmdk" onClick={e => { if (e.target === e.currentTarget) setOpen(false); }}>
    <div className="cmdk-box" role="dialog" aria-modal="true" aria-label="Быстрый переход">
      <div className="shift-line" style={{paddingLeft: 16}}><Search size={18} aria-hidden="true"/>
        <input ref={input} value={q} onChange={e => { setQ(e.target.value); setActive(0); }} onKeyDown={keyDown} placeholder="Раздел: касса, авансы, отчёты…" role="combobox" aria-expanded="true" aria-controls="cmdk-list" aria-activedescendant={found[active] ? 'cmdk-' + found[active][0] : undefined} aria-label="Поиск раздела"/></div>
      <ul id="cmdk-list" role="listbox">{found.map((item, i) => { const [id, label, Icon, group] = item; return <li key={id} id={'cmdk-' + id} role="option" aria-selected={i === active} onMouseEnter={() => setActive(i)} onClick={() => choose(item)}><Icon size={18} aria-hidden="true"/><span style={{flex: 1}}>{label}</span><small className="muted">{group}</small></li>; })}
        {!found.length && <li aria-disabled="true" className="muted">Ничего не найдено</li>}</ul>
      <p className="field-hint" style={{padding: '0 16px 12px'}}><kbd>↑</kbd> <kbd>↓</kbd> выбор · <kbd>Enter</kbd> перейти · <kbd>Esc</kbd> закрыть</p>
    </div>
  </div>;
}
