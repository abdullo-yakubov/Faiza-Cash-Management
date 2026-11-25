import React, {useId, useRef, useState} from 'react';
import {UploadCloud, Camera} from 'lucide-react';
import {validateFiles, ATTACHMENT_TYPES, ATTACHMENT_MAX_BYTES, ATTACHMENT_MAX_FILES, fileSizeText} from '../../utils/files.js';

/**
 * Зона загрузки: drag-and-drop, выбор файла и «Сфотографировать» (камера телефона).
 * onFiles(acceptedFiles) вызывается только с прошедшими клиентскую проверку файлами.
 */
export function FileDrop({onFiles, existing = 0, types = ATTACHMENT_TYPES, maxBytes = ATTACHMENT_MAX_BYTES, maxFiles = ATTACHMENT_MAX_FILES, multiple = true, camera = true, disabled, label = 'Перетащите сканы или фото документов сюда'}) {
  const [over, setOver] = useState(false), [errors, setErrors] = useState([]);
  const fileRef = useRef(null), cameraRef = useRef(null), hintId = useId();
  const accept = types.join(',');
  function take(list) {
    const {accepted, errors} = validateFiles([...list], {existing, types, maxBytes, maxFiles});
    setErrors(errors);
    if (accepted.length) onFiles(accepted);
  }
  const reset = e => { take(e.target.files || []); e.target.value = ''; };
  return <div className={'ui-filedrop' + (over ? ' over' : '') + (disabled ? ' disabled' : '')}
    onDragOver={e => { if (disabled) return; e.preventDefault(); setOver(true); }}
    onDragLeave={() => setOver(false)}
    onDrop={e => { e.preventDefault(); setOver(false); if (!disabled) take(e.dataTransfer.files); }}>
    <UploadCloud size={28} aria-hidden="true"/>
    <p><strong>{label}</strong></p>
    <p id={hintId} className="field-hint">{types.map(t => t.split('/')[1].toUpperCase()).join(', ')} · до {fileSizeText(maxBytes)} · не больше {maxFiles} файлов{existing ? ` (уже ${existing})` : ''}</p>
    <div className="row-actions">
      <button type="button" className="button secondary" disabled={disabled} aria-describedby={hintId} onClick={() => fileRef.current?.click()}>Выбрать файл</button>
      {camera && <button type="button" className="button secondary" disabled={disabled} onClick={() => cameraRef.current?.click()}><Camera size={18} aria-hidden="true"/>Сфотографировать</button>}
    </div>
    <input ref={fileRef} type="file" hidden accept={accept} multiple={multiple} onChange={reset} tabIndex={-1}/>
    {camera && <input ref={cameraRef} type="file" hidden accept="image/*" capture="environment" onChange={reset} tabIndex={-1}/>}
    {errors.length > 0 && <ul className="ui-filedrop-errors" role="alert">{errors.map(e => <li key={e}>{e}</li>)}</ul>}
  </div>;
}
