import React, {useState} from 'react';
import {Database, HardDrive, ShieldAlert, Users, Archive, Activity, KeyRound, UserX, ArrowRight, RotateCcw} from 'lucide-react';
import {api} from '../services/api.js';
import {adminHealth} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {Button, Card, StatusPill, SkeletonBlock, ErrorState, EmptyState} from '../components/ui';
import {dateTimeText} from '../utils/format.js';
import {fileSizeText} from '../utils/files.js';

function HealthCard({icon: Icon, title, tone, status, children}) {
  return <article className="health-card"><h3><Icon size={18} aria-hidden="true"/>{title}{status && <span style={{marginLeft: 'auto'}}><StatusPill tone={tone}>{status}</StatusPill></span>}</h3>{children}</article>;
}

/** Панель администратора (контракт п.8): состояние системы и лента аудита. */
export function AdminPage({onGrantAccess, onNotice}) {
  const health = useAsync(signal => adminHealth(signal), []);
  const audit = useAsync(signal => api('/audit?' + new URLSearchParams({page: 1, pageSize: 8}), {signal}), []);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  async function backupNow() {
    setBusy(true); setError('');
    try { await api('/backups', {method: 'POST'}); onNotice?.('Резервная копия создана.'); health.reload(); }
    catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  const h = health.data;
  return <div className="ui-stack">
    {error && <div className="error" role="alert">{error}</div>}
    {health.status === 'error' ? <section className="panel"><ErrorState error={health.error} onRetry={health.reload} title="Состояние системы недоступно"/></section> : !h ? <SkeletonBlock tiles={6} rows={0}/> : <>
      <div className="health-grid">
        <HealthCard icon={Database} title="База данных" tone={h.database.ok && !h.database.pendingMigrations ? 'success' : 'danger'} status={h.database.ok ? (h.database.pendingMigrations ? 'Есть миграции' : 'Работает') : 'Недоступна'}>
          <span className="big">PostgreSQL {h.database.version || '—'}</span>
          <p>Неприменённых миграций: {h.database.pendingMigrations}</p>
        </HealthCard>
        <HealthCard icon={Archive} title="Резервная копия" tone={h.lastBackup.status === 'ok' ? (h.lastBackup.verified ? 'success' : 'warning') : h.lastBackup.status === 'failed' ? 'danger' : 'warning'} status={{ok: h.lastBackup.verified ? 'Проверена' : 'Не проверена', failed: 'Ошибка', none: 'Нет копий'}[h.lastBackup.status]}>
          <span className="big">{h.lastBackup.at ? dateTimeText(h.lastBackup.at) : '—'}</span>
          <div className="row-actions"><Button size="sm" icon={Archive} loading={busy} onClick={backupNow}>Создать копию сейчас</Button><a className="text-btn" href="#/backups">Все копии</a></div>
        </HealthCard>
        <HealthCard icon={HardDrive} title="Хранилище">
          <span className="big">{fileSizeText(h.storage.freeDiskBytes)}</span>
          <p>свободно на диске · вложения занимают {fileSizeText(h.storage.attachmentsBytes)}</p>
        </HealthCard>
        <HealthCard icon={Activity} title="Активные сеансы"><span className="big">{h.activeSessions}</span><p>действующих refresh-токенов</p></HealthCard>
        <HealthCard icon={KeyRound} title="Неудачные входы за сутки" tone={h.failedLogins24h > 10 ? 'danger' : h.failedLogins24h > 0 ? 'warning' : 'success'} status={h.failedLogins24h > 10 ? 'Подозрительно' : h.failedLogins24h > 0 ? 'Внимание' : 'Норма'}>
          <span className="big">{h.failedLogins24h}</span><a className="text-btn" href="#/audit?action=login_failed">Открыть в журнале аудита</a>
        </HealthCard>
        <HealthCard icon={ShieldAlert} title="Версия приложения"><span className="big">{h.appVersion}</span><p>Время сервера — Душанбе (UTC+5)</p></HealthCard>
      </div>
      <Card title="Пользователи без доступа к кассам" subtitle="Эти сотрудники не увидят ни одной кассы, пока им не выдан доступ">
        {h.usersWithoutCashAccess.length ? <ul className="ui-list">{h.usersWithoutCashAccess.map(u => <li key={u.id}><UserX size={18} aria-hidden="true"/><span className="grow">{u.fullName}</span><Button size="sm" variant="secondary" icon={ArrowRight} onClick={() => onGrantAccess(u)}>Выдать доступ</Button></li>)}</ul>
          : <EmptyState icon={Users} title="Все сотрудники имеют доступ" text="У каждого активного пользователя назначена хотя бы одна касса."/>}
      </Card>
    </>}
    <Card title="Последние события аудита" actions={<><button type="button" className="icon-btn" aria-label="Обновить ленту аудита" title="Обновить" onClick={audit.reload}><RotateCcw size={18}/></button><a className="text-btn" href="#/audit">Весь журнал</a></>}>
      {audit.status === 'error' ? <ErrorState error={audit.error} onRetry={audit.reload}/> : !audit.data ? <SkeletonBlock rows={4}/> : (audit.data.items || []).length ? <ul className="ui-list">{audit.data.items.map(e => <li key={e.id}>
        <div className="grow"><strong>{e.actionName || e.action}</strong><small>{e.entityName || e.entity}{e.entityId ? ' №' + e.entityId : ''} · {e.userName || e.user || 'Система'}</small></div>
        <small>{dateTimeText(e.createdAt)}</small>
      </li>)}</ul> : <EmptyState title="Событий нет"/>}
    </Card>
  </div>;
}
