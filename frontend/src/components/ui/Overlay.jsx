import React, {useEffect, useId, useRef} from 'react';
import {X} from 'lucide-react';
import {Dialog} from '../Dialog.jsx';

/** Модальное окно — единый компонент поверх существующего доступного Dialog. */
export const Modal = Dialog;

const focusable = 'button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),a[href],[tabindex]:not([tabindex="-1"])';

/** Боковая панель (карточка записи). Esc закрывает, фокус удерживается внутри. */
export function Drawer({title, subtitle, children, footer, onClose, busy, width = 560}) {
  const ref = useRef(null), close = useRef(onClose), titleId = useId();
  close.current = onClose;
  useEffect(() => {
    const previous = document.activeElement, overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    ref.current?.querySelector(focusable)?.focus();
    return () => { document.body.style.overflow = overflow; if (previous?.isConnected) previous.focus(); };
  }, []);
  function keyDown(e) {
    if (e.key === 'Escape' && !busy) { e.stopPropagation(); close.current(); }
    if (e.key === 'Tab') {
      const nodes = [...ref.current.querySelectorAll(focusable)].filter(n => n.getClientRects().length);
      const first = nodes[0], last = nodes.at(-1);
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
    }
  }
  return <div className="ui-drawer-backdrop" onClick={e => { if (e.target === e.currentTarget && !busy) onClose(); }}>
    <aside className="ui-drawer" ref={ref} role="dialog" aria-modal="true" aria-labelledby={titleId} onKeyDown={keyDown} style={{'--drawer-width': width + 'px'}}>
      <header className="ui-drawer-head"><div><h2 id={titleId}>{title}</h2>{subtitle && <p>{subtitle}</p>}</div><button type="button" className="icon-btn" aria-label="Закрыть панель" title="Закрыть" disabled={busy} onClick={onClose}><X size={20}/></button></header>
      <div className="ui-drawer-body">{children}</div>
      {footer && <footer className="ui-drawer-foot">{footer}</footer>}
    </aside>
  </div>;
}
