// Разделение обязанностей (волна 1): admin больше не проводит финансовые операции.
// Наборы тестов создают кассы и пользователей от bootstrap-администратора, а финансовые
// сценарии исторически выполняли от него же. Чтобы не переписывать сотни вызовов, тестовая
// учётная запись получает дополнительную роль accountant (admin+accountant) и новый токен.
// Отдельные тесты (p0.mjs) проверяют, что «чистый» admin получает 403 на финансовых маршрутах.
export async function grantFinanceRole(base, token, login, password, cookieJar) {
  const headers = {'Content-Type': 'application/json', 'X-Faiza-Request': '1', Authorization: 'Bearer ' + token};
  const me = await (await fetch(base + '/api/auth/me', {headers})).json();
  const patched = await fetch(base + '/api/users/' + me.id, {method: 'PATCH', headers, body: JSON.stringify({roles: ['admin', 'accountant']})});
  if (patched.status !== 204) throw new Error('Не удалось выдать тестовому администратору роль accountant: ' + patched.status);
  const response = await fetch(base + '/api/auth/login', {method: 'POST', headers: {'Content-Type': 'application/json', 'X-Faiza-Request': '1'}, body: JSON.stringify({login, password})});
  if (response.status !== 200) throw new Error('Повторный вход тестового администратора не удался: ' + response.status);
  if (cookieJar) cookieJar.cookie = response.headers.get('set-cookie')?.split(';')[0];
  const data = await response.json();
  return {token: data.accessToken, user: data.user};
}
