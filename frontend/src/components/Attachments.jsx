import React, {useEffect, useState} from 'react';
import {FileText, Download, Trash2, Eye, Paperclip} from 'lucide-react';
import {attachments as attachmentApi} from '../services/endpoints.js';
import {useAsync} from '../hooks/useAsync.js';
import {FileDrop, Modal, SkeletonBlock, ErrorState, Button} from './ui';
import {fileSizeText, IMAGE_TYPES, ATTACHMENT_MAX_FILES} from '../utils/files.js';
import {dateTimeText} from '../utils/format.js';

function Thumb({item, onOpen}) {
  const [url, setUrl] = useState(null);
  useEffect(() => {
    if (!IMAGE_TYPES.includes(item.contentType)) return;
    const controller = new AbortController();
    let objectUrl;
    attachmentApi.content(item.id, controller.signal).then(blob => { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); }).catch(() => {});
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [item.id, item.contentType]);
  return <button type="button" className="attachment-thumb" onClick={onOpen} aria-label={'Просмотр: ' + item.fileName}>
    {url ? <img src={url} alt=""/> : <FileText size={36} aria-hidden="true"/>}
  </button>;
}

function Preview({item, onClose}) {
  const [state, setState] = useState({url: null, error: ''});
  useEffect(() => {
    let objectUrl;
    attachmentApi.content(item.id).then(blob => { objectUrl = URL.createObjectURL(blob); setState({url: objectUrl, type: blob.type || item.contentType, error: ''}); }).catch(e => setState({url: null, error: e.message}));
    return () => { if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [item.id, item.contentType]);
  return <Modal title={item.fileName} onClose={onClose} footer={<><Button variant="secondary" icon={Download} onClick={() => attachmentApi.download(item)}>Скачать</Button><Button variant="secondary" onClick={onClose}>Закрыть</Button></>}>
    {state.error ? <div className="error" role="alert">{state.error}</div> : !state.url ? <SkeletonBlock rows={2}/> :
      (state.type || '').startsWith('image/') ? <img className="attachment-preview" src={state.url} alt={'Скан документа: ' + item.fileName}/> :
      <iframe className="attachment-preview-frame" src={state.url} title={'PDF: ' + item.fileName}/>}
  </Modal>;
}

/**
 * Вложения документа: список с превью, загрузка и удаление (пока владелец — черновик).
 * canManage — уже учитывает роль и статус владельца.
 */
export function Attachments({ownerType, ownerId, canManage, title = 'Сканы и фото документов'}) {
  const list = useAsync(signal => attachmentApi.list(ownerType, ownerId, signal), [ownerType, ownerId]);
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [preview, setPreview] = useState(null);
  const items = list.data || [];
  async function add(files) {
    setBusy(true); setError('');
    try { for (const file of files) await attachmentApi.add(ownerType, ownerId, file); list.reload(); }
    catch (e) { setError(e.message); list.reload(); }
    finally { setBusy(false); }
  }
  async function remove(item) {
    if (!window.confirm(`Удалить вложение «${item.fileName}»?`)) return;
    setBusy(true); setError('');
    try { await attachmentApi.remove(item.id); list.reload(); } catch (e) { setError(e.message); } finally { setBusy(false); }
  }
  return <section className="attachments" aria-label={title}>
    <h4 className="shift-line"><Paperclip size={16} aria-hidden="true"/>{title}{list.status === 'success' && <span className="muted">· {items.length}</span>}</h4>
    {list.status === 'loading' && !list.data ? <SkeletonBlock rows={1}/> : list.status === 'error' ? <ErrorState error={list.error} onRetry={list.reload} title="Не удалось загрузить вложения"/> : <>
      {items.length > 0 ? <ul className="attachment-list">{items.map(item => <li key={item.id} className="attachment-item">
        <Thumb item={item} onOpen={() => setPreview(item)}/>
        <div className="attachment-meta"><strong title={item.fileName}>{item.fileName}</strong><span>{fileSizeText(item.sizeBytes)} · {dateTimeText(item.uploadedAt)}</span><span>{item.uploadedBy}</span></div>
        <div className="attachment-actions">
          <button type="button" className="icon-btn" aria-label={'Открыть ' + item.fileName} title="Открыть" onClick={() => setPreview(item)}><Eye size={17}/></button>
          <button type="button" className="icon-btn" aria-label={'Скачать ' + item.fileName} title="Скачать" onClick={() => attachmentApi.download(item).catch(e => setError(e.message))}><Download size={17}/></button>
          {canManage && <button type="button" className="icon-btn" aria-label={'Удалить ' + item.fileName} title="Удалить" disabled={busy} onClick={() => remove(item)}><Trash2 size={17}/></button>}
        </div>
      </li>)}</ul> : !canManage && <p className="muted">Вложений нет.</p>}
      {canManage && items.length < ATTACHMENT_MAX_FILES && <FileDrop existing={items.length} onFiles={add} disabled={busy}/>}
      {!canManage && items.length > 0 && <p className="field-hint">Документ проведён или у вас нет прав: вложения доступны только для просмотра.</p>}
    </>}
    {busy && <p className="action-progress" role="status">Загрузка файла…</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {preview && <Preview item={preview} onClose={() => setPreview(null)}/>}
  </section>;
}
