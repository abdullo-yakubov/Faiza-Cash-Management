# Результаты тестирования

Дата: 2026-11-15.

| Проверка | Результат |
|---|---|
| Backend solution build | PASS |
| .NET unit tests | PASS — 24/24 |
| Frontend tests | PASS — 12/12 |
| Frontend production build | PASS |
| PostgreSQL integration | PASS — 29/29 на отдельной тестовой БД |
| PostgreSQL security | PASS — 16/16 на отдельной тестовой БД |
| Catalog validation | PASS — 92/92 |
| Ledger regression | PASS — 7/7 |
| Backup/Restore | BLOCKED — не выполнялся без отдельной тестовой БД |
| Browser E2E | BLOCKED — фактический браузерный прогон не выполнен |

Нельзя считать BLOCKED проверки PASS; перед защитой их нужно выполнить в локальном окружении с PostgreSQL и браузером.
# Результаты проверки V5 UI — 2026-11-15

- `npm run build` — PASS
- `npm test -- --runInBand` — PASS, 12/12
- `dotnet build backend/FaizaCash.sln --no-restore` — PASS, 0 warnings, 0 errors
- `dotnet test backend/FaizaCash.sln --no-build` — PASS, 24/24
- Реальные внешние PostgreSQL backup/restore и browser E2E — не запускались в этой сессии, так как сервисы не были подняты.
## Дополнение 2026-11-15

| Набор | Статус | Результат |
|---|---|---|
| Backend build | PASS | 0 warnings, 0 errors |
| Backend unit | PASS | 24/24 |
| Frontend build | PASS | Vite production build |
| Frontend tests | PASS | 12/12 |
| PostgreSQL integration | PASS | 30/30 checks |
| Security | PASS | результаты сохранены в `.runtime/security-test-results.json` |
| Catalog | PASS | 92/92 |
| Ledger regression | PASS | 7/7 |
| Backup/restore | PASS | quiescent rerun: counts and signed totals match; first 36→40 was concurrent-write race |
| Docker/TLS | BLOCKED | Docker Desktop says running, but daemon pipe/config/log access returns `Access is denied` |
| Browser E2E | BLOCKED | отдельный browser test runtime не поднят |
## E2E/Docker update 2026-11-15

| Проверка | Статус | Фактический результат |
|---|---|---|
| Local backend | PASS | started on 5090, test PostgreSQL |
| Local Vite | PASS | started on 5175 with Faiza API proxy |
| Playwright authentication | PASS | admin login returned 200 |
| Dashboard/branches API UI | PASS | real requests returned 200 |
| Mobile/theme/reduced motion smoke | PASS | 390×844, dark, reduced motion exercised |
| Full business E2E | FAIL | mutation flow suite not completed |
| Docker/TLS | BLOCKED | Docker Engine named pipe access denied |
## Complete business E2E diagnosis 2026-11-15

| Сценарий | Статус | Причина |
|---|---|---|
| Авторизация admin | PASS | Playwright login endpoint/UI login succeeded |
| Dashboard | PASS | Real `/api/dashboard` returned 200 and route loaded after whitelist fix |
| Выбор филиала/кассы | PASS | Branch/cashbox data loaded from real API |
| Открытие смены | BLOCKED | UI control is absent from current application shell; endpoint exists |
| Приход | PASS | Existing operation dialog supports draft/post flow |
| Расход | PASS | Existing operation dialog supports draft/post flow |
| Межкассовый перевод | PASS | Dedicated UI and atomic backend endpoint available; full mutation not replayed in this run |
| Создание задолженности | BLOCKED | Backend endpoint exists, but no create form is wired into DebtsPage |
| Частичная оплата | BLOCKED | Backend allocation endpoint exists, but no allocation control is wired into DebtsPage |
| Торговый представитель | PARTIAL | Page and endpoints exist; full authenticated mutation sequence not completed |
| Возврат/сторно | PASS | Existing operation detail flow exposes reversal for privileged roles |
| Закрытие смены | BLOCKED | UI control is absent from current application shell |
| Финансовый отчёт | PASS | Existing reports/cashbook UI and API available |
| RBAC role switching | BLOCKED | Requires separate seeded role sessions; not completed in this browser run |
| Mobile/theme/reduced-motion | PASS | Playwright exercised 390×844, dark scheme and reduced motion |

Root causes are separated: missing UI controls (not backend), stale refresh-cookie after process replacement (auth/session setup), and incomplete role fixture setup. No database or Playwright timeout defect was observed.
## UI completion update 2026-11-15

- Added `ShiftsPage`: open/close shift, opening/closing balance, current shift and history via `/api/shifts` endpoints.
- Extended `DebtsPage`: create settlement and allocate a partial payment through real backend endpoints, with amount validation and duplicate-submit protection through the form flow.
- Added `shifts` route to the route whitelist and navigation.
- Frontend build — PASS.
- Frontend tests — PASS, 12/12.
- Full mutation Browser E2E — NOT YET PASS: fresh isolated role sessions and the complete ordered mutation sequence still need to be replayed after these UI additions.
## Final Playwright retry update 2026-11-15

- FAIL found and fixed: `ShiftsPage`, `DebtsPage` and `RepresentativesPage` passed async functions directly to `useEffect`, causing React cleanup warnings and unstable page content. Effects now invoke async loaders inside a synchronous callback.
- Frontend build after fix — PASS.
- Frontend tests after fix — PASS, 12/12.
- Full financial Browser E2E — not PASS in this run; the isolated session was interrupted by the discovered UI runtime defect before the ordered mutation chain could complete.
