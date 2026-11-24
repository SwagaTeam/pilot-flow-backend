# Pilot Flow Backend

Backend выбора пилотного потока на ASP.NET Core 10. Соседний репозиторий: [frontend](https://github.com/SwagaTeam/pilot-flow-frontend).

Реализованы 45 операций OpenAPI. API ведёт анализ предприятия от черновика с продукцией, маршрутами и семействами до расчёта рейтинга, фиксации решения и выгрузки PDF.

## Деплой на сервер

Пошаговая инструкция: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). Production-стек включает PostgreSQL, отдельный мигратор, API и Caddy с автоматическим HTTPS; управление — `./scripts/deploy.sh`. До запуска нужны домен и настоящий OIDC-провайдер.

## Быстрый запуск

Нужен .NET SDK 10.0.400 или совместимая версия, разрешённая `global.json`.

```powershell
dotnet restore PilotFlow.Backend.slnx
dotnet run --project src/PilotFlow.Api
```

Профиль по умолчанию поднимает API на `http://localhost:5037`:

- `GET /health/live` — процесс работает;
- `GET /health/ready` — база доступна, миграция применена и у API есть доступ к таблице анализов. Без доступной БД возвращается `503`;
- `/swagger` — автономный Swagger UI без CDN;
- `/swagger/openapi.json` и `/swagger/openapi.yaml` — авторитетный контракт из `contracts/`.

Health checks и Swagger доступны без аутентификации. Бизнес-операции требуют Bearer JWT с claim `sub`. Задайте `Jwt__Authority`, `Jwt__Audience` и `ConnectionStrings__PilotFlow` в окружении при запуске через `dotnet run`. В Production эти настройки обязательны при старте.

Возьмите тело `POST /api/v1/analyses` из примера Swagger. После создания используйте `Location` для чтения анализа и значения `id` и `version.id` для обращения к версии. Для `PUT` передайте объект `data` целиком и последний `ETag` в заголовке `If-Match`. Отсутствующий заголовок даёт `428 IF_MATCH_REQUIRED`, некорректный — `400 INVALID_REQUEST`, устаревший — `412 REVISION_CONFLICT`. Чужой анализ даёт `404 NOT_FOUND`. Списки принимают `offset`, `limit` и поиск `q` по названию.

Внутри версии запросы к `products`, `operations` и `products/{productId}/route` возвращают ETag версии. После любого успешного изменения используйте новый ETag. Поиск продукции работает также по коду, а `completeness=complete|incomplete|all` фильтрует по полноте. В маршруте можно повторять операцию с отдельными `occurrenceId` и соседними позициями. Используемая в маршруте операция удаляется только после удаления ссылок на неё.

В `family-suggestions` доступны предложения по маршрутам. Состав сохраняется через `assignments`, показатели потока — через `families/{familyId}/metrics`. Читайте `evaluation`, `family-matrix` и `direct-selection-options` для проверки расчётов и допустимых вариантов выбора.

После сохранения `selection` и `protocol` проверьте `validation` и `protocol-preview`. Запрос `POST .../finalize` фиксирует версию и создаёт решение с SHA-256 снимка. Чтобы вернуться к редактированию, создайте черновик из зафиксированной версии через `POST .../fork`.

PDF черновика доступен через `GET .../versions/{versionId}/export`, а PDF принятого решения — через `GET .../decisions/{decisionId}/export`. Параметр `document` выбирает комплект, протокол или одну из матриц. Образ Docker уже содержит шрифт DejaVu Sans. Для запуска вне Docker PDF использует системный Arial на Windows и DejaVu Sans на Linux.

## Проверки

```powershell
dotnet format PilotFlow.Backend.slnx --verify-no-changes
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
```

Интеграционные тесты требуют запущенного Docker с Linux-контейнерами. Они поднимают отдельный PostgreSQL с временными данными. HTTP- и контрактные тесты можно запускать по их проектам без Docker.

## Docker

Для просмотра Swagger можно запустить один API-контейнер.

```powershell
docker compose up --build -d --wait api
```

Compose запускает API в окружении Development. Swagger доступен на [localhost:8080/swagger](http://localhost:8080/swagger). Для остановки используйте `docker compose down`.

Workflow `.github/workflows/ci.yml` запускает Release-тесты для push и pull request в `main`, включая проверки с PostgreSQL. Для будущей публикации в Docker Hub понадобятся имя репозитория, политика тегов и токен с доступом к репозиторию.

### PostgreSQL и миграции

Для работы с данными задайте параметры в локальном `.env` или в окружении.

- `PILOTFLOW_DB_PASSWORD` — пароль локального администратора PostgreSQL.
- `PILOTFLOW_APP_DB_PASSWORD` — отдельный пароль роли `pilot_app`, от которой работает API.
- `PILOTFLOW_API_CONNECTION_STRING` — параметры Npgsql без пароля. Для Compose укажите `Host=postgres`, `Database=pilot_flow`, `Username=pilot_app` и `GSS Encryption Mode=Disable`, разделив их точкой с запятой.
- `PILOTFLOW_JWT_AUTHORITY` и `PILOTFLOW_JWT_AUDIENCE` — настройки поставщика Bearer-токенов для запросов из Swagger.

```powershell
docker compose --profile migration run --build --rm grant-api
docker compose up --build -d --wait api
Invoke-WebRequest http://localhost:8080/health/ready
```

Compose дождётся готовности PostgreSQL и завершения миграций. Затем `grant-api` создаёт локальную роль `pilot_app` и выдаёт права для рабочих операций. При обновлении существующего стека повторите эти команды, чтобы применить V002 и обновить права. Данные сохраняются в томе `postgres-data`. Для существующего тома используйте пароль, с которым он был инициализирован.

Мигратор запускается отдельно от API. Для внешней БД передайте строку подключения через `PILOTFLOW_MIGRATION_CONNECTION_STRING` и выполните `dotnet run --project src/PilotFlow.DbMigrator`. Миграция V001 предназначена для пустой базы. Для последующих изменений добавляйте следующий SQL-файл (`V002.sql` и далее) в `src/PilotFlow.Infrastructure/Persistence/Migrations/`. Транзакцией управляет мигратор. История хранится в `pilot_migrations.history`. Изменение уже применённого файла приводит к ошибке контрольной суммы.

Для внешней БД администратор создаёт отдельную роль `pilot_app` и после миграций применяет `src/PilotFlow.Infrastructure/Persistence/Security/analysis-grants.sql`. Подключайте API под этой ролью. Роль мигратора нужна только для изменения схемы.

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
