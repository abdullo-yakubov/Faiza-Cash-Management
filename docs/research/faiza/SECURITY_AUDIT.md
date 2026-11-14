# Аудит безопасности

## PASS по коду и тестам

- Production требует `RefreshCookie:Secure=true` и доверенный список proxy: `backend/src/FaizaCash.Api/Program.cs:16–29`.
- Forwarded headers применяются до HTTPS redirection: `Program.cs:58–60`.
- Production compose отделяет мигратора от runtime DB role и не публикует PostgreSQL наружу: `docker-compose.production.yml:18–71,98–109`.
- CSRF, Origin, refresh rotation, role isolation и доступ к кассам подтверждены 14 security checks из `node tests/run-all.mjs`.

## BLOCKED / не доказано

Live TLS-сертификат, DNS, внешний reverse proxy и реальный `Set-Cookie` через production edge не проверены. Секреты, реальные серверы и сертификаты не изменялись.
