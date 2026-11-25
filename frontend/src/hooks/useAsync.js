import {useCallback, useEffect, useRef, useState} from 'react';

/**
 * Загрузка данных компонента: { status: 'loading'|'success'|'error', data, error, reload }.
 * Ответ на устаревший запрос (сменился key) отбрасывается.
 */
export function useAsync(loader, deps = [], enabled = true) {
  const [state, setState] = useState({status: 'loading', data: null, error: null});
  const [attempt, setAttempt] = useState(0);
  const latest = useRef(loader);
  latest.current = loader;
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    let current = true;
    setState(s => ({status: 'loading', data: s.data, error: null}));
    Promise.resolve().then(() => latest.current(controller.signal))
      .then(data => { if (current) setState({status: 'success', data, error: null}); })
      .catch(error => { if (current && error?.name !== 'AbortError') setState({status: 'error', data: null, error}); });
    return () => { current = false; controller.abort(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, attempt, enabled]);
  const reload = useCallback(() => setAttempt(v => v + 1), []);
  return {...state, reload};
}
