# Pilot Flow — Backend

Backend: **ASP.NET Core / C#, PostgreSQL**. Соседний репозиторий: [frontend](https://github.com/SwagaTeam/pilot-flow-frontend).

Репозиторий содержит авторитетные контракты и схему для реализации ASP.NET-приложения.

## Запуск и материалы

- `contracts/schema.sql`: применить один раз к пустой БД: `psql -v ON_ERROR_STOP=1 -d pilot_db -f contracts/schema.sql`.
- `contracts/reference/handoff.html`: правила, архитектура, спорные требования и распределение работ.
- `contracts/openapi.yaml` / `contracts/openapi.json`: контракт 1.0.0, 45 операций, 51 схема.
- `contracts/reference/swagger.html`: локальный Swagger UI; запуск через mock описан ниже.
- `contracts/control-example.json`, `contracts/acceptance-vectors.json`: эталонные данные и граничные случаи.
- `contracts/reference/validation-report.json`: результаты проверки схемы и контракта.
- `contracts/reference/pilot-flow-contract-v1.zip`: полный первоначальный пакет.

Для просмотра фикстур запустите `node contracts/reference/mock-server.mjs`, затем откройте `http://localhost:5080/swagger.html`.

## Ответственность backend

Реализовать DTO и endpoints по OpenAPI; хранение и миграции; расчёт допуска и рейтинга; проверку полноты; ETag/If-Match; авторизацию владельца; неизменяемые снимки решения; PDF-экспорт. Детали в `contracts/reference/handoff.html`.

## Единый контракт

Авторитетная версия OpenAPI хранится в `contracts/`. Frontend содержит побайтовую копию версии 1.0.0. Любые изменения контракта выпускаются согласованно в обоих репозиториях до изменения клиентов. Не менять значения enum, nullable и decimal-строки независимо.

Проверены 313 примеров API и 19 проверок БД (PGlite). Производственный ASP.NET сервер ещё не тестировался.

`contracts/reference/LICENSE` и `contracts/reference/NOTICE` относятся к Swagger UI 5.18.2, а не назначают лицензию всему проекту.
