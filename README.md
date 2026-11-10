# Pilot Flow Backend

Backend выбора пилотного потока на ASP.NET Core 10. Соседний репозиторий: [frontend](https://github.com/SwagaTeam/pilot-flow-frontend).

Готовы HTTP-каркас и первая управляемая миграция PostgreSQL. Каркас регистрирует 45 операций из OpenAPI, проверяет JWT и форму `If-Match`, отдаёт Swagger и служебные проверки. Рабочие операции будут реализованы в тонких контроллерах по ресурсам.

## Быстрый запуск

Нужен .NET SDK 10.0.400 или совместимая версия, разрешённая `global.json`.

```powershell
dotnet restore PilotFlow.Backend.slnx
dotnet run --project src/PilotFlow.Api
```

Профиль по умолчанию поднимает API на `http://localhost:5037`:

- `GET /health/live` — процесс работает;
- `GET /health/ready` — каркас готов принимать запросы;
- `/swagger` — автономный Swagger UI без CDN;
- `/swagger/openapi.json` и `/swagger/openapi.yaml` — авторитетный контракт из `contracts/`.

Health checks и Swagger доступны без аутентификации. Все 45 бизнес-операций требуют Bearer JWT с claim `sub`. Для Production обязательны настройки `Jwt__Authority` и `Jwt__Audience`; секреты в репозитории не хранятся.

На этом этапе бизнес-операции намеренно не реализованы: после успешной аутентификации они отвечают `501 application/problem+json` с кодом `NOT_IMPLEMENTED`. Мутации агрегата сначала проверяют один сильный `If-Match` формата `"v:{versionId}:r:{revision}"` и возвращают `428 ETAG_REQUIRED` либо `400 INVALID_ETAG`, если заголовок отсутствует или некорректен.

## Проверки

```powershell
dotnet format PilotFlow.Backend.slnx --verify-no-changes
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
```

Интеграционные тесты требуют запущенного Docker с Linux-контейнерами. Они поднимают отдельный PostgreSQL с временными данными. HTTP- и контрактные тесты можно запускать по их проектам без Docker.

## Docker

Запустите API локально через Docker Compose.

```powershell
docker compose up --build -d --wait api
```

Compose запускает API в окружении Development. Swagger доступен на [localhost:8080/swagger](http://localhost:8080/swagger). Для остановки используйте `docker compose down`.

Workflow `.github/workflows/ci.yml` запускает Release-тесты для push и pull request в `main`, включая проверки с PostgreSQL. Для будущей публикации в Docker Hub понадобятся имя репозитория, политика тегов и токен с доступом к репозиторию.

### PostgreSQL и миграции

Задайте `PILOTFLOW_DB_PASSWORD` в локальном файле `.env` или в окружении. Затем запустите PostgreSQL 18.7 и примените схему.

```powershell
docker compose --profile migration run --build --rm migrate
```

Compose дождётся готовности PostgreSQL и запустит мигратор. Локальная база называется `pilot_flow`, пользователь `pilot_dev_admin` предназначен для разработки. PostgreSQL доступен внутри сети Compose. Данные сохраняются в томе `postgres-data`. Для уже созданного тома используйте пароль, с которым он был инициализирован.

Мигратор запускается отдельно от API. Для внешней БД передайте строку подключения через `PILOTFLOW_MIGRATION_CONNECTION_STRING` и выполните `dotnet run --project src/PilotFlow.DbMigrator`. Миграция V001 предназначена для пустой базы. Для последующих изменений добавляйте следующий SQL-файл (`V002.sql` и далее) в `src/PilotFlow.Infrastructure/Persistence/Migrations/`. Транзакцией управляет мигратор. История хранится в `pilot_migrations.history`. Изменение уже применённого файла приводит к ошибке контрольной суммы.

HTTP-каркас пока работает без подключения к БД. Проверка доступности PostgreSQL, отображения EF Core и транзакционные операции появятся вместе со слоем хранения.

## Контракты и исходные материалы

- `contracts/openapi.yaml` и `contracts/openapi.json` — OpenAPI 3.0.3 для API версии 1.0.0, 45 операций и 51 схема;
- `contracts/schema.sql` — исходная схема PostgreSQL, на которой основана миграция V001;
- `contracts/control-example.json` и `contracts/acceptance-vectors.json` — контрольный пример и граничные случаи;
- `contracts/reference/handoff.html` — правила, архитектура и спорные требования;
- `contracts/reference/validation-report.json` — результаты проверки контракта;
- `contracts/reference/mock-server.mjs` — прежний mock для просмотра фикстур.

Авторитетные OpenAPI-файлы не дублируются в проекте API: сборка связывает и побайтово копирует их в publish output. Изменения контракта должны выпускаться согласованно с frontend; нельзя независимо менять enum, nullable-поля и decimal-строки.

Для просмотра прежнего mock выполните `node contracts/reference/mock-server.mjs` и откройте `http://localhost:5080/swagger.html`.

`contracts/reference/LICENSE` и `contracts/reference/NOTICE` относятся к Swagger UI 5.18.2 и не задают лицензию всему проекту.
