// Тема интерфейса: 'system' | 'light' | 'dark'. Хранится в localStorage (в try/catch:
// приватный режим и запрет cookies не должны ломать приложение).
const KEY = 'faiza-theme';
export const THEMES = ['system', 'light', 'dark'];
export const themeLabels = {system: 'системная', light: 'светлая', dark: 'тёмная'};

export function readTheme() {
  try { const value = globalThis.localStorage?.getItem(KEY); return THEMES.includes(value) ? value : 'system'; } catch { return 'system'; }
}
export function saveTheme(value) {
  try { globalThis.localStorage?.setItem(KEY, value); } catch { /* хранилище недоступно — тема действует до перезагрузки */ }
}
export function resolveTheme(value, prefersDark) {
  return value === 'system' ? (prefersDark ? 'dark' : 'light') : value;
}
export const nextTheme = value => THEMES[(THEMES.indexOf(value) + 1) % THEMES.length];
