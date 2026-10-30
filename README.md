# Pilot Flow Backend

Backend выбора пилотного потока на ASP.NET Core 10. Соседний репозиторий: [frontend](https://github.com/SwagaTeam/pilot-flow-frontend).

Сейчас готов первый исполняемый каркас: слои решения, HTTP-хост, JWT-граница, 45 операций из OpenAPI, строгая форма `If-Match`, health checks и локальный Swagger UI. База данных и бизнес-логика будут добавлены следующими этапами.

## Быстрый запуск

Нужен .NET SDK 10.0.400 или совместимая версия, разрешённая `global.json`.

```powershell
dotnet restore PilotFlow.Backend.slnx
dotnet run --project src/PilotFlow.Api
```

Профиль по умолчанию поднимает API на `http://localhost:5037`:

- `GET /health/live` — процесс работает;
- `GET /health/ready` — каркас готов принимать запросы;
- `/swagger/swagger.html` — автономный Swagger UI без CDN;
- `/swagger/openapi.json` и `/swagger/openapi.yaml` — авторитетный контракт из `contracts/`.

Health checks и Swagger доступны без аутентификации. Все 45 бизнес-операций требуют Bearer JWT с claim `sub`. Для Production обязательны настройки `Jwt__Authority` и `Jwt__Audience`; секреты в репозитории не хранятся.

На этом этапе бизнес-операции намеренно не реализованы: после успешной аутентификации они отвечают `501 application/problem+json` с кодом `NOT_IMPLEMENTED`. Мутации агрегата сначала проверяют один сильный `If-Match` формата `"v:{versionId}:r:{revision}"` и возвращают `428 ETAG_REQUIRED` либо `400 INVALID_ETAG`, если заголовок отсутствует или некорректен.

## Проверки

```powershell
dotnet format PilotFlow.Backend.slnx --verify-no-changes
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
```

## Контракты и исходные материалы

- `contracts/openapi.yaml` и `contracts/openapi.json` — OpenAPI 1.0.0, 45 операций и 51 схема;
- `contracts/schema.sql` — проект схемы PostgreSQL для следующего этапа;
- `contracts/control-example.json` и `contracts/acceptance-vectors.json` — контрольный пример и граничные случаи;
- `contracts/reference/handoff.html` — правила, архитектура и спорные требования;
- `contracts/reference/validation-report.json` — результаты проверки контракта;
- `contracts/reference/mock-server.mjs` — прежний mock для просмотра фикстур.

Авторитетные OpenAPI-файлы не дублируются в проекте API: сборка связывает и побайтово копирует их в publish output. Изменения контракта должны выпускаться согласованно с frontend; нельзя независимо менять enum, nullable-поля и decimal-строки.

Для просмотра прежнего mock выполните `node contracts/reference/mock-server.mjs` и откройте `http://localhost:5080/swagger.html`.

`contracts/reference/LICENSE` и `contracts/reference/NOTICE` относятся к Swagger UI 5.18.2 и не задают лицензию всему проекту.
