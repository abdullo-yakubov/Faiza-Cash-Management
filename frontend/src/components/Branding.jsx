import React, {useEffect, useState} from 'react';
import {api} from '../services/api.js';

/** Картинка из API (логотип, аватар) → object URL. 404 → null. version сбрасывает кэш после загрузки. */
export function useApiImage(path, version = 0, enabled = true) {
  const [url, setUrl] = useState(null);
  useEffect(() => {
    if (!enabled || !path) { setUrl(null); return; }
    const controller = new AbortController();
    let objectUrl;
    api(path, {raw: true, signal: controller.signal, retry: false})
      .then(blob => { if (blob && blob.size) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } else setUrl(null); })
      .catch(() => setUrl(null));
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [path, version, enabled]);
  return url;
}

export const initials = name => String(name || '').trim().split(/\s+/).slice(0, 2).map(p => p[0]?.toUpperCase() || '').join('') || 'С';

/** Аватар пользователя: фото или инициалы. */
export function UserAvatar({user, version = 0, size}) {
  const url = useApiImage(user?.id ? `/users/${user.id}/avatar` : null, version);
  return <span className={'avatar' + (size === 'lg' ? ' avatar-lg' : '')} aria-hidden="true">{url ? <img src={url} alt=""/> : initials(user?.fullName)}</span>;
}

/** Логотип организации (если загружен). */
export function OrgLogo({version = 0, className = 'org-logo', alt = 'Логотип организации'}) {
  const url = useApiImage('/settings/logo', version);
  return url ? <img className={className} src={url} alt={alt}/> : null;
}
