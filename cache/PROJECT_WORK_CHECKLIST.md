# Рабочий лист запуска и проверки FAIZA GOLD

## Выполнено

- Backend restore: успешно.
- Backend build: успешно, 0 ошибок и 0 предупреждений.
- Backend unit-тесты: 24/24 успешно.
- API health endpoint: `http://localhost:5080/health` отвечает `200`.
- Frontend логические тесты: 10/10 успешно.
- Markdown-документация проекта создана в `cache/FAIZA_GOLD_PROJECT_TZ.md`.

## Требуется выполнить после освобождения Windows-блокировки Node-бинарников

```powershell
cd frontend
npm ci
npm test
npm run build
```

Ожидаемый результат: 11 frontend-тестов успешно и production-сборка Vite без ошибок.

## Полный локальный запуск

```powershell
cd ..
dotnet run --project backend/src/FaizaCash.Api
```

В отдельном окне:

```powershell
cd frontend
npm run dev
```

Проверка API:

```powershell
Invoke-WebRequest http://localhost:5080/health
```

## Docker-запуск

Docker Engine должен быть запущен. После этого:

```powershell
docker compose --env-file .env up -d --build
docker compose --env-file .env ps
```

Проверка:

- API: `http://localhost:5080/health`
- frontend: `http://localhost:8080`

## Ограничения среды

- Docker Engine в текущем окружении недоступен через `docker_engine`.
- `frontend/node_modules` содержит заблокированный Windows бинарник Rollup; `npm ci` не может его заменить.
- PostgreSQL и Docker volumes не удалять.
- Секреты из `.env` не публиковать и не коммитить.
