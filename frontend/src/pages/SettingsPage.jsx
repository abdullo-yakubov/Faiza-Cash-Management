import React, {useRef, useState} from 'react';
import {Upload, Trash2, KeyRound, ImageOff} from 'lucide-react';
import {settings as settingsApi, branding} from '../services/endpoints.js';
import {Button, Card, Field} from '../components/ui';
import {OrgLogo, UserAvatar, useApiImage} from '../components/Branding.jsx';
import {validateFiles, IMAGE_TYPES, LOGO_MAX_BYTES} from '../utils/files.js';
import {normalizeAmount, money} from '../utils/format.js';
import {can} from '../utils/permissions.js';
import {rolesText} from '../utils/format.js';

function ImageUpload({label, onFile, disabled}) {
  const ref = useRef(null);
  return <>
    <Button variant="secondary" icon={Upload} disabled={disabled} onClick={() => ref.current?.click()}>{label}</Button>
    <input ref={ref} type="file" hidden accept={IMAGE_TYPES.join(',')} tabIndex={-1} onChange={e => { const file = e.target.files?.[0]; e.target.value = ''; if (file) onFile(file); }}/>
  </>;
}

/** Настройки: организация, порог согласования, логотип, аватар, пароль. */
export function SettingsPage({org, user, onSaved, onPassword, brandVersion, onBrandChange, onNotice}) {
  const admin = can(user, 'settings.manage');
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [thresholdError, setThresholdError] = useState('');
  const hasLogo = Boolean(useApiImage('/settings/logo', brandVersion));
  async function run(action, message) {
    setBusy(true); setError('');
    try { await action(); onNotice?.(message); onBrandChange?.(); } catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  function imageFile(file, action, message) {
    const {accepted, errors} = validateFiles([file], {types: IMAGE_TYPES, maxBytes: LOGO_MAX_BYTES, maxFiles: 1});
    if (errors.length) { setError(errors[0]); return; }
    run(() => action(accepted[0]), message);
  }
  async function saveOrg(e) {
    e.preventDefault(); setThresholdError('');
    const data = Object.fromEntries(new FormData(e.currentTarget));
    let threshold = null;
    if (data.approvalEnabled === 'on') { try { threshold = normalizeAmount(data.threshold); } catch (err) { setThresholdError(err.message); return; } }
    setBusy(true); setError('');
    try { await settingsApi.save({name: data.name, currency: 'TJS', expenseApprovalThreshold: threshold}); onSaved?.('Настройки сохранены.'); }
    catch (err) { setError(err.message); } finally { setBusy(false); }
  }
  const [approvalOn, setApprovalOn] = useState(org.expenseApprovalThreshold != null);
  return <div className="ui-stack settings-page">
    {error && <div className="error" role="alert">{error}</div>}
    <Card title="Организация" subtitle="Учёт в сомони (TJS). Часовой пояс — Душанбе (UTC+5).">
      <form onSubmit={saveOrg} className="ui-stack">
        <Field label="Название организации *"><input name="name" required maxLength={160} defaultValue={org.name} disabled={!admin}/></Field>
        <Field label="Валюта учёта"><input value="TJS — сомони" readOnly aria-describedby="currency-hint"/></Field>
        <p id="currency-hint" className="field-hint">Касса работает в одной валюте. Изменение валюты после начала учёта запрещено.</p>
        <fieldset className="roles-field"><legend>Согласование расходов</legend>
          <label className="role-option"><input type="checkbox" name="approvalEnabled" checked={approvalOn} disabled={!admin} onChange={e => setApprovalOn(e.target.checked)}/><span><strong>Требовать согласование руководителя</strong><small>Расход на сумму не меньше порога можно провести только после согласования. Любое изменение черновика сбрасывает согласование.</small></span></label>
          {approvalOn && <Field label="Порог согласования, TJS *" error={thresholdError} errorId="threshold-error"><input name="threshold" inputMode="decimal" disabled={!admin} defaultValue={org.expenseApprovalThreshold || ''} placeholder="Например, 5 000,00" aria-invalid={Boolean(thresholdError)}/></Field>}
          {!approvalOn && <p className="field-hint">Согласование выключено: порог не задан.</p>}
          {org.expenseApprovalThreshold && <p className="field-hint">Сейчас: расходы от {money(org.expenseApprovalThreshold)} TJS требуют согласования.</p>}
        </fieldset>
        {admin ? <div><Button type="submit" loading={busy}>Сохранить настройки</Button></div> : <p className="muted">Настройки организации изменяет администратор.</p>}
      </form>
    </Card>
    <Card title="Логотип организации" subtitle="Показывается в шапке, на странице входа и в PDF-отчётах. PNG, JPEG или WebP до 1 МБ.">
      <div className="settings-row">
        {hasLogo ? <OrgLogo version={brandVersion} className="org-logo-preview"/> : <span className="muted shift-line"><ImageOff size={18} aria-hidden="true"/>Логотип не загружен — используется фирменный знак FAIZA GOLD.</span>}
        {can(user, 'logo.manage') && <div className="row-actions">
          <ImageUpload label={hasLogo ? 'Заменить логотип' : 'Загрузить логотип'} disabled={busy} onFile={file => imageFile(file, branding.uploadLogo, 'Логотип обновлён.')}/>
          {hasLogo && <Button variant="danger" icon={Trash2} disabled={busy} onClick={() => window.confirm('Удалить логотип организации?') && run(branding.removeLogo, 'Логотип удалён.')}>Удалить</Button>}
        </div>}
      </div>
    </Card>
    <Card title="Ваш профиль" subtitle={`${user.fullName} · ${rolesText(user.roles)}`}>
      <div className="settings-row">
        <UserAvatar user={user} version={brandVersion} size="lg"/>
        <div className="row-actions">
          <ImageUpload label="Загрузить фото" disabled={busy} onFile={file => imageFile(file, branding.uploadAvatar, 'Аватар обновлён.')}/>
          <Button variant="secondary" icon={Trash2} disabled={busy} onClick={() => run(branding.removeAvatar, 'Аватар удалён, показываются инициалы.')}>Убрать фото</Button>
        </div>
      </div>
      <p className="field-hint">Без фото в меню показываются инициалы. PNG, JPEG или WebP до 1 МБ.</p>
      <hr/>
      <h3>Безопасность учётной записи</h3>
      <p className="muted">Изменение пароля завершит возможность обновления прежних сеансов.</p>
      <Button variant="secondary" icon={KeyRound} onClick={onPassword}>Сменить пароль</Button>
    </Card>
    <Card title="О системе"><p className="muted">Интерфейс React передаёт запросы в API ASP.NET Core. Бизнес-правила проверяют операцию, PostgreSQL хранит документы и аудит. Учебное приложение; документы не являются официальными бухгалтерскими формами.</p></Card>
  </div>;
}
