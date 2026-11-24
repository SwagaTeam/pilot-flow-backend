# Деплой на один VPS

## Что купить и настроить

Рекомендуемый старт: Ubuntu 24.04 LTS, x86_64, 2 vCPU, 4 ГБ RAM, 40 ГБ SSD, публичный IPv4. Это начальная конфигурация, не результат нагрузочного теста. Сборка выполняется на сервере. Понадобится домен API, например `api.your-domain.ru`, и DNS A-запись на IP сервера. AAAA добавляйте только при рабочем IPv6. Разрешите входящие TCP 80/443, SSH — только с доверенных адресов. Порты PostgreSQL и API наружу не публикуются.

**До пользовательского запуска нужен настоящий OIDC-провайдер.** Бэкенд проверяет JWT по HTTPS issuer и audience, но не реализует регистрацию, вход и выдачу токенов. Текущий фронтенд принимает Bearer JWT вручную. Произвольный URL в `PILOTFLOW_JWT_AUTHORITY` не создаёт сервер авторизации. Для полноценного входа требуется отдельная настройка провайдера и интеграция фронтенда. Health checks без токена этого не проверяют.

## Первый запуск

Перенесите проверенные исходники в `/opt/pilot-flow` (включая скрытые файлы и без локальных `.env`, `bin`, `obj`, `backups`). Команды ниже выполняются на новом сервере с правами root:

```bash
cd /opt/pilot-flow
bash scripts/bootstrap-ubuntu.sh
bash scripts/deploy.sh init
nano deploy/.env
bash scripts/deploy.sh check
bash scripts/deploy.sh up
```

Заполните домен API, точный HTTPS origin фронтенда без завершающего `/`, настоящий issuer и audience. Пароли двух ролей БД генерируются автоматически. Сохраните `deploy/.env` отдельно в защищённом хранилище. Если файл уже существует, `init` откажется его перезаписывать. Не переносите тестовые настройки на сервер.

`up` собирает образы, запускает БД, сохраняет резервную копию, останавливает API на время миграций, применяет миграции и права, запускает API и Caddy. Caddy получает и обновляет сертификат автоматически при доступных DNS и портах 80/443. Внутренняя проверка готовности не гарантирует доступность HTTPS из интернета:

```bash
curl --fail https://YOUR_API_DOMAIN/health/ready
bash scripts/deploy.sh status
bash scripts/deploy.sh logs
```

Затем откройте фронтенд, подключите API с настоящим JWT, создайте анализ, перечитайте его, проверьте сохранение после перезапуска и скачивание PDF. Запрос без токена должен возвращать 401. CORS разрешает только заданный origin.

## Обновление и сбои

Сохраните предыдущие исходники и значение `PILOTFLOW_RELEASE`. Перенесите новую проверенную версию, оставив `deploy/.env` и `backups`, задайте новый уникальный `PILOTFLOW_RELEASE`, выполните `check` и `up`. Предусмотрен короткий простой, автоматического отката нет. Не меняйте пароли в `.env` существующей БД: смена переменной не меняет пароль PostgreSQL.

При ошибке миграции запуск прекращается, API может остаться остановленным. Изучите вывод и `logs`, исправьте причину и повторите `up`. Не редактируйте применённые SQL-миграции. Старую версию API можно запускать только после проверки совместимости с новой схемой; иначе восстановите старые исходники и предмиграционную копию на новом сервере/томе. Не используйте `docker compose down -v`: это удаляет данные.

## Резервные копии и восстановление

```bash
bash scripts/deploy.sh backup
bash scripts/deploy.sh restore-test /opt/pilot-flow/backups/FILE.dump
install -m 644 deploy/pilot-flow-backup.service deploy/pilot-flow-backup.timer /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now pilot-flow-backup.timer
systemctl list-timers pilot-flow-backup.timer
```

Таймер делает копию ежедневно в 03:00 UTC с задержкой до 15 минут (08:00–08:15 Екатеринбург). Проверяйте ошибки через `journalctl -u pilot-flow-backup.service`. Копии остаются в `backups/`; автоматического удаления и отправки вне сервера нет. Настройте отдельное защищённое хранилище и контроль свободного места. Копия на том же диске не защищает от потери VPS.

На новом сервере с пустой БД подготовьте исходники и конфигурацию, перенесите dump:

```bash
bash scripts/deploy.sh restore-empty /secure/path/FILE.dump
bash scripts/deploy.sh up
```

`restore-empty` отказывается заменять существующие схемы приложения. `restore-test` восстанавливает копию во временную БД и удаляет только её. Dump содержит данные и историю миграций, но не конфигурацию, пароли или TLS-тома.

## Проверка до сервера

```bash
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet format PilotFlow.Backend.slnx --verify-no-changes
dotnet test PilotFlow.Backend.slnx --configuration Release
python3 scripts/smoke.py
```

Нужны .NET 10, Docker и Compose 2.24.4+ (smoke использует `!override`). Smoke создаёт отдельный случайный Compose-проект: проверяет сборку production-образов, повторные миграции, Caddy, readiness, отказ без JWT, CORS, backup/restore и перезапуск. В конце удаляет только собственные тестовые контейнеры и тома. Выпуск публичного сертификата и вход через реальный OIDC проверяются после настройки домена и провайдера.

## Текущий VPS и CI/CD

VPS: Debian 13, 2 ГБ RAM, 25 ГБ диска, 2 ГБ swap. API: `https://wooden-chrysopal29118.my-vm.work`. Установка Docker для этой ОС: `bash scripts/bootstrap-debian.sh`.

Workflow `CI` выполняет тесты приложения и smoke production-контейнеров. Только после обеих успешных проверок push в `main` (или ручной запуск workflow для `main`) передаёт исходники точного коммита на VPS по SSH. Параллельные деплои сериализованы. Pull request не получает доступ к VPS. Не отключайте проверки перед деплоем.

GitHub Actions требует два repository secret: `VPS_DEPLOY_KEY` (отдельный приватный ключ) и `VPS_KNOWN_HOSTS` (проверенный публичный host key сервера). Пользователь `pilot-deploy` допускает только forced command `/usr/local/sbin/pilot-flow-receive`; интерактивный shell, forwarding и PTY для этого ключа запрещены. Получатель проверяет размер и пути архива, создаёт отдельный каталог релиза, запускает миграции и deployment и только после успеха переключает `/opt/pilot-flow/current`. Секреты и резервные копии хранятся вне релиза. Хотя SSH-команда ограничена, право публиковать код production фактически даёт контроль над приложением и его данными: защищайте доступ на запись в `main` и Actions secrets.

Первоначальная установка находится в `/opt/pilot-flow`, релизы CI — в `/opt/pilot-flow/releases/`. Общий файл API-конфигурации: `/opt/pilot-flow/deploy/.env`; общие бэкапы: `/opt/pilot-flow/backups`. Для команд нового релиза задавайте `PILOTFLOW_ENV_FILE=/opt/pilot-flow/deploy/.env` и `PILOTFLOW_BACKUP_DIR=/opt/pilot-flow/backups`.

Вместе с API используется Keycloak (`compose.auth.yaml`) и отдельный PostgreSQL без опубликованных портов. Адрес issuer: `https://wooden-chrysopal29118.my-vm.work/auth/realms/pilot-flow`, audience/client: `pilot-flow-api`. Конфигурация и начальные учётные данные находятся только на сервере в `deploy/auth.env`, `deploy/auth-realm.json`, `deploy/access.txt`. Не добавляйте их в Git. Caddy автоматически выпускает сертификаты без email контакта.

Сервис авторизации запускается отдельно после создания сети основным стеком:

```bash
cd /opt/pilot-flow
docker compose --env-file deploy/auth.env -f compose.auth.yaml up -d
```

Фронтенд пока принимает Bearer вручную. Начальная учётная запись `pilot-owner` предназначена владельцу, регистрация отключена. Для получения короткоживущего токена используйте `scripts/get-token.py`: пароль вводится скрыто, токен выводится только по явному запуску этой команды. Для полноценного пользовательского входа фронтенду требуется Authorization Code + PKCE; текущий временный direct grant следует отключить после интеграции.

Данные Keycloak находятся в отдельном томе `pilot-flow-auth_auth-data`. Их нужно сохранять отдельным `pg_dump` сервиса `auth-db` наряду с основной БД. Копии на самом VPS не заменяют внешнее хранилище.
