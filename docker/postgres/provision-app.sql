\getenv app_password PILOTFLOW_APP_DB_PASSWORD

SELECT 'CREATE ROLE pilot_app'
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'pilot_app')
\gexec

SELECT pg_catalog.format(
    'ALTER ROLE pilot_app LOGIN NOSUPERUSER NOCREATEROLE NOCREATEDB NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L',
    :'app_password')
\gexec
