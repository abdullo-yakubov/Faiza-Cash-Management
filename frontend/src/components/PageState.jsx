import React from 'react';
import {LockKeyhole, LogIn} from 'lucide-react';
import {SkeletonBlock, ErrorState, EmptyState, Button} from './ui';

// Три состояния страницы: загрузка (скелетоны), ошибка (текст + «Повторить»), данные.
export function PageState({state, onRetry, onLogin, children}) {
  if (state.status === 'loading') return <SkeletonBlock tiles={3} rows={4} label="Загрузка данных…"/>;
  if (state.status === 'error') {
    const code = state.error?.status;
    if (code === 403) return <section className="panel"><EmptyState icon={LockKeyhole} title="Недостаточно прав" text={state.error.message}/></section>;
    if (code === 401) return <section className="panel"><EmptyState icon={LogIn} title="Сеанс завершён" text={state.error.message} action={<Button icon={LogIn} onClick={onLogin}>Войти повторно</Button>}/></section>;
    return <section className="panel"><ErrorState error={state.error} onRetry={onRetry}/></section>;
  }
  return children;
}
