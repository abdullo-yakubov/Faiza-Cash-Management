import React from 'react';
import {Inbox, AlertTriangle, RotateCcw, Loader2, Check, Clock, X, FileEdit, ArrowDownLeft, ArrowUpRight} from 'lucide-react';
import {money} from '../../utils/format.js';

const cx = (...parts) => parts.filter(Boolean).join(' ');

/** Кнопка. variant: primary | secondary | danger | ghost; size: md | lg | sm. */
export function Button({variant = 'primary', size = 'md', icon: Icon, loading = false, children, className, type = 'button', disabled, ...rest}) {
  return <button type={type} className={cx('ui-btn', 'ui-btn-' + variant, size !== 'md' && 'ui-btn-' + size, className)} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
    {loading ? <Loader2 size={18} className="ui-spin" aria-hidden="true"/> : Icon ? <Icon size={size === 'lg' ? 22 : 18} aria-hidden="true"/> : null}
    {children}
  </button>;
}

/** Иконка-кнопка: aria-label обязателен. */
export function IconButton({label, icon: Icon, className, ...rest}) {
  return <button type="button" className={cx('icon-btn', className)} aria-label={label} title={label} {...rest}><Icon size={19} aria-hidden="true"/></button>;
}

/** Карточка-панель с заголовком и действиями. */
export function Card({title, subtitle, actions, children, className, as: Tag = 'section', id, padded = true, ...rest}) {
  const headingId = id ? id + '-title' : undefined;
  return <Tag className={cx('panel', 'ui-card', className)} aria-labelledby={title ? headingId : undefined} id={id} {...rest}>
    {(title || actions) && <div className="panel-heading"><div>{title && <h2 id={headingId}>{title}</h2>}{subtitle && <p>{subtitle}</p>}</div>{actions && <div className="row-actions">{actions}</div>}</div>}
    <div className={padded ? 'ui-card-body' : undefined}>{children}</div>
  </Tag>;
}

const toneIcons = {income: ArrowDownLeft, expense: ArrowUpRight};

/**
 * KPI-плитка. kind: money | count. tone: neutral | income | expense | warning | danger | accent.
 * Цвет — не единственный носитель смысла: у прихода/расхода есть знак и иконка.
 */
export function StatTile({label, value, currency, kind = 'money', tone = 'neutral', icon, hint, href, onClick, loading, wide}) {
  const Icon = icon || toneIcons[tone];
  const sign = kind === 'money' && tone === 'income' ? '+ ' : kind === 'money' && tone === 'expense' ? '− ' : '';
  const body = <>
    <span className="ui-stat-label">{Icon && <span className={'ui-stat-icon tone-' + tone}><Icon size={17} aria-hidden="true"/></span>}{label}</span>
    {loading ? <Skeleton width="60%" height={28}/> : <strong className={'ui-stat-value tone-' + tone}>{sign}{kind === 'money' ? money(value) : (value ?? '—')}{kind === 'money' && currency ? <small> {currency}</small> : null}</strong>}
    {hint && <span className="ui-stat-hint">{hint}</span>}
  </>;
  if (href) return <a className={'ui-stat ui-stat-link tone-' + tone + (wide ? ' ui-stat-wide' : '')} href={href}>{body}</a>;
  if (onClick) return <button type="button" className={'ui-stat ui-stat-link tone-' + tone + (wide ? ' ui-stat-wide' : '')} onClick={onClick}>{body}</button>;
  return <div className={'ui-stat tone-' + tone + (wide ? ' ui-stat-wide' : '')}>{body}</div>;
}

/** Статус-бейдж. tone: success | warning | danger | info | neutral | accent. */
export function StatusPill({tone = 'neutral', icon: Icon, children, title}) {
  return <span className={'ui-pill tone-' + tone} title={title}>{Icon && <Icon size={13} aria-hidden="true"/>}{children}</span>;
}

const approvalMap = {
  pending: ['warning', Clock, 'На согласовании'],
  approved: ['success', Check, 'Согласовано'],
  rejected: ['danger', X, 'Отклонено']
};
export function ApprovalPill({status}) {
  const item = approvalMap[status];
  return item ? <StatusPill tone={item[0]} icon={item[1]}>{item[2]}</StatusPill> : null;
}
export function OperationStatusPill({status}) {
  return status === 'posted' ? <StatusPill tone="success" icon={Check}>Проведено</StatusPill> : <StatusPill tone="neutral" icon={FileEdit}>Черновик</StatusPill>;
}

/** Скелетон-заглушка. */
export function Skeleton({width = '100%', height = 14, radius, className}) {
  return <span className={cx('ui-skeleton', className)} style={{width, height, borderRadius: radius}} aria-hidden="true"/>;
}
export function SkeletonBlock({rows = 4, tiles = 0, label = 'Загрузка данных…'}) {
  return <div className="ui-skeleton-block" role="status" aria-live="polite">
    <span className="sr-only">{label}</span>
    {tiles > 0 && <div className="ui-tiles">{Array.from({length: tiles}, (_, i) => <div className="ui-stat" key={i}><Skeleton width="45%"/><Skeleton width="70%" height={28}/></div>)}</div>}
    {rows > 0 && <div className="panel ui-card-body ui-skeleton-rows">{Array.from({length: rows}, (_, i) => <div className="ui-skeleton-row" key={i}><Skeleton width={36} height={36} radius={10}/><div><Skeleton width={`${60 - (i % 3) * 12}%`}/><Skeleton width="30%" height={11}/></div><Skeleton width={90}/></div>)}</div>}
  </div>;
}

/** Пустое состояние: иконка + текст + действие. */
export function EmptyState({icon: Icon = Inbox, title, text, action}) {
  return <div className="ui-empty">
    <span className="ui-empty-icon"><Icon size={28} aria-hidden="true"/></span>
    {title && <h3>{title}</h3>}
    {text && <p>{text}</p>}
    {action}
  </div>;
}

/** Ошибка загрузки: текст + «Повторить». */
export function ErrorState({error, onRetry, title = 'Не удалось загрузить данные'}) {
  return <div className="ui-empty ui-error-state" role="alert">
    <span className="ui-empty-icon tone-danger"><AlertTriangle size={28} aria-hidden="true"/></span>
    <h3>{title}</h3>
    <p>{typeof error === 'string' ? error : error?.message || 'Повторите попытку.'}</p>
    {onRetry && <Button variant="secondary" icon={RotateCcw} onClick={onRetry}>Повторить</Button>}
  </div>;
}

/** Обёртка трёх состояний для данных из useAsync/usePageData. */
export function AsyncView({state, onRetry, skeleton, empty, isEmpty, children}) {
  if (state.status === 'loading' || state.status === 'idle') return skeleton || <SkeletonBlock/>;
  if (state.status === 'error') return <ErrorState error={state.error} onRetry={onRetry}/>;
  if (isEmpty?.(state.data)) return empty || <EmptyState title="Записей нет"/>;
  return children(state.data);
}

/** Денежная сумма с цветом, знаком и табличными цифрами. */
export function Amount({value, type, currency, strong = true}) {
  const sign = type === 'income' ? '+' : type === 'expense' ? '−' : '';
  const Tag = strong ? 'strong' : 'span';
  return <Tag className={cx('ui-amount', type && 'tone-' + type)}>{sign && <span aria-hidden="true">{sign} </span>}<span className="sr-only">{type === 'income' ? 'приход ' : type === 'expense' ? 'расход ' : ''}</span>{money(value)}{currency && <small> {currency}</small>}</Tag>;
}
