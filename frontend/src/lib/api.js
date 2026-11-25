// Обёртка над fetch: cookie входа, JSON и понятные ошибки.
export class ApiError extends Error {
  constructor(message, status) {
    super(message)
    this.status = status
  }
}

let onUnauthorized = () => {}
export function setUnauthorizedHandler(fn) {
  onUnauthorized = fn
}

export async function api(path, { method = 'GET', body, raw = false } = {}) {
  const res = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (res.status === 401 && !path.startsWith('/auth/login')) onUnauthorized()
  if (!res.ok) {
    let message = `Ошибка ${res.status}`
    try {
      const data = await res.json()
      message = data.title || data.detail || message
    } catch {
      /* ответ без JSON */
    }
    if (res.status === 403 && message.startsWith('Ошибка')) message = 'Недостаточно прав для этого действия.'
    if (res.status === 429) message = 'Слишком много попыток. Подождите минуту.'
    throw new ApiError(message, res.status)
  }
  if (raw) return res
  if (res.status === 204) return null
  return res.json()
}

export async function download(path, fallbackName) {
  const res = await api(path, { raw: true })
  const blob = await res.blob()
  const disposition = res.headers.get('Content-Disposition') || ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = match ? decodeURIComponent(match[1]) : fallbackName
  a.click()
  URL.revokeObjectURL(url)
}
