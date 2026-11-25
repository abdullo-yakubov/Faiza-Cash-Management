// Клиентская проверка вложений до отправки. Сервер — главный (он проверяет сигнатуру файла),
// здесь — быстрые подсказки пользователю, чтобы не гонять заведомо неверные файлы.
export const ATTACHMENT_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf'];
export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
export const ATTACHMENT_MAX_BYTES = 5 * 1024 * 1024;
export const ATTACHMENT_MAX_FILES = 10;
export const LOGO_MAX_BYTES = 1024 * 1024;

const byExtension = {jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', pdf: 'application/pdf'};
export const guessType = file => file.type || byExtension[String(file.name).split('.').pop().toLowerCase()] || '';

export function fileSizeText(bytes) {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return bytes + ' Б';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + ' КБ';
  if (bytes < 1024 ** 3) return (bytes / 1024 / 1024).toFixed(1).replace('.', ',') + ' МБ';
  return (bytes / 1024 ** 3).toFixed(1).replace('.', ',') + ' ГБ';
}

/**
 * files — массив File-подобных объектов {name, size, type}.
 * → { accepted: File[], errors: string[] }
 */
export function validateFiles(files, {existing = 0, types = ATTACHMENT_TYPES, maxBytes = ATTACHMENT_MAX_BYTES, maxFiles = ATTACHMENT_MAX_FILES} = {}) {
  const accepted = [], errors = [];
  for (const file of files) {
    const type = guessType(file);
    if (!types.includes(type)) { errors.push(`«${file.name}»: недопустимый тип. Разрешены ${types.map(t => t.split('/')[1].toUpperCase().replace('JPEG', 'JPEG')).join(', ')}.`); continue; }
    if (file.size > maxBytes) { errors.push(`«${file.name}»: размер ${fileSizeText(file.size)} больше ${fileSizeText(maxBytes)}.`); continue; }
    if (file.size === 0) { errors.push(`«${file.name}»: пустой файл.`); continue; }
    if (existing + accepted.length >= maxFiles) { errors.push(`Можно приложить не больше ${maxFiles} файлов к одному документу.`); break; }
    accepted.push(file);
  }
  return {accepted, errors};
}
