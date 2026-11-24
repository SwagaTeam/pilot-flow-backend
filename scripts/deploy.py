#!/usr/bin/env python3
"""Single-server deployment. Uses only Python's standard library and Docker Compose."""
import argparse
import datetime
import fcntl
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
ENV = Path(os.environ.get("PILOTFLOW_ENV_FILE", ROOT / "deploy/.env")).resolve()
BACKUPS = Path(os.environ.get("PILOTFLOW_BACKUP_DIR", ROOT / "backups")).resolve()


def run(args, **kwargs):
    return subprocess.run(args, check=True, cwd=ROOT, **kwargs)


def read_env():
    if not ENV.is_file():
        raise ValueError(f"Missing {ENV}; run scripts/deploy.sh init")
    values = {}
    for line in ENV.read_text().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, sep, value = line.partition("=")
        if not sep or key != key.strip() or not re.fullmatch(r"[A-Z_]+", key):
            raise ValueError("Use simple KEY=value lines in the environment file")
        if key in values:
            raise ValueError(f"Duplicate setting: {key}")
        values[key] = value
    # Prevent shell environment overriding the reviewed config in Compose.
    for key in values:
        os.environ.pop(key, None)
    return values


def validate(values):
    required = ("PILOTFLOW_DOMAIN", "PILOTFLOW_FRONTEND_ORIGIN",
                "PILOTFLOW_JWT_AUTHORITY", "PILOTFLOW_JWT_AUDIENCE",
                "PILOTFLOW_DB_PASSWORD", "PILOTFLOW_APP_DB_PASSWORD", "PILOTFLOW_RELEASE")
    for key in required:
        value = values.get(key, "")
        if not value or any(c.isspace() for c in value) or any(c in value for c in "'\"$#\\"):
            raise ValueError(f"Set a nonempty plain value for {key}")
    domain = values["PILOTFLOW_DOMAIN"]
    if not re.fullmatch(r"[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?", domain) or "." not in domain:
        raise ValueError("PILOTFLOW_DOMAIN must be a DNS name without scheme, port or path")
    if domain.endswith((".example.com", ".invalid")) or domain == "example.com":
        raise ValueError("Replace the example domain with your API domain")
    for key in ("PILOTFLOW_JWT_AUTHORITY", "PILOTFLOW_FRONTEND_ORIGIN"):
        url = urlsplit(values[key])
        if url.scheme != "https" or not url.hostname or url.username or url.password or url.query or url.fragment:
            raise ValueError(f"{key} must be an HTTPS URL without credentials, query or fragment")
        if url.hostname.endswith((".example.com", ".invalid")):
            raise ValueError(f"Replace example value for {key}")
        if key == "PILOTFLOW_FRONTEND_ORIGIN" and url.path:
            raise ValueError("Frontend origin must not contain a path or trailing slash")
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,100}", values["PILOTFLOW_RELEASE"]):
        raise ValueError("Use a short Docker tag as PILOTFLOW_RELEASE")
    for key in ("PILOTFLOW_DB_PASSWORD", "PILOTFLOW_APP_DB_PASSWORD"):
        if not re.fullmatch(r"[0-9a-f]{64}", values[key]):
            raise ValueError(f"{key} must be a 64-character random hex password; init generates it")
    if values["PILOTFLOW_DB_PASSWORD"] == values["PILOTFLOW_APP_DB_PASSWORD"]:
        raise ValueError("Database administrator and API passwords must differ")
    if ENV.stat().st_mode & 0o077:
        raise ValueError(f"Restrict configuration permissions: chmod 600 {ENV}")


def compose_command():
    if subprocess.run(["docker", "compose", "version"], stdout=subprocess.DEVNULL,
                      stderr=subprocess.DEVNULL).returncode == 0:
        command = ["docker", "compose"]
    elif shutil.which("docker-compose"):
        command = ["docker-compose"]
    else:
        raise ValueError("Install Docker Compose v2 (2.20+) or newer")
    return command + ["--project-name", "pilot-flow", "--env-file", str(ENV),
                      "-f", str(ROOT / "compose.production.yaml")]


def dc(*args, **kwargs):
    return run(compose_command() + list(args), **kwargs)


def db(*args, **kwargs):
    return dc("exec", "-T", "postgres", *args, **kwargs)


def init():
    text = (ROOT / "deploy/.env.example").read_text()
    for key in ("PILOTFLOW_DB_PASSWORD", "PILOTFLOW_APP_DB_PASSWORD"):
        text = text.replace(f"{key}=\n", f"{key}={secrets.token_hex(32)}\n")
    ENV.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(ENV, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w") as output:
        output.write(text)
    print(f"Created {ENV} with independent passwords. Fill domain, email, frontend and OIDC settings.")


def backup():
    BACKUPS.mkdir(mode=0o700, exist_ok=True)
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    target = BACKUPS / f"pilot-flow-{stamp}-{secrets.token_hex(3)}.dump"
    fd, temp = tempfile.mkstemp(prefix=".partial-", dir=BACKUPS)
    try:
        with os.fdopen(fd, "wb") as output:
            db("pg_dump", "-U", "pilot_admin", "-d", "pilot_flow", "-Fc", "--no-owner", "--no-privileges", stdout=output)
            output.flush()
            os.fsync(output.fileno())
        with open(temp, "rb") as source:
            db("pg_restore", "--list", stdin=source, stdout=subprocess.DEVNULL)
        os.replace(temp, target)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    print(f"Backup: {target}")
    return target


def restore_test(path):
    # Disposable database only: this does not modify pilot_flow.
    name = "pilot_restore_" + secrets.token_hex(8)
    db("createdb", "-U", "pilot_admin", name)
    try:
        with path.open("rb") as source:
            db("pg_restore", "-U", "pilot_admin", "-d", name, "--exit-on-error",
               "--single-transaction", "--no-owner", "--no-privileges", stdin=source)
        db("psql", "-X", "-U", "pilot_admin", "-d", name, "-v", "ON_ERROR_STOP=1",
           "-c", "SELECT count(*) AS migrations FROM pilot_migrations.history; SELECT count(*) AS analyses FROM pilot.analysis;")
    finally:
        db("dropdb", "-U", "pilot_admin", name)
    print("Restore verified in an isolated temporary database.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["init", "check", "up", "status", "logs", "backup", "restore-test", "restore-empty"])
    parser.add_argument("file", nargs="?", type=Path)
    args = parser.parse_args()
    if args.command == "init":
        init()
        return
    values = read_env()
    validate(values)
    dc("config", "--quiet")
    if args.command == "check":
        print("Configuration validated; secrets were not printed. OIDC and DNS still need live verification.")
        return
    if args.command == "status":
        dc("ps", "-a")
        return
    if args.command == "logs":
        dc("logs", "--tail", "100", "api", "caddy", "postgres")
        return
    # Serializes deploys and backups on this host without leaving a stale lock.
    with (ENV.parent / ".operation.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if args.command == "up":
            # Fetch/build while the previous API is still serving.
            dc("build", "--pull", "api", "migrate")
            dc("pull", "postgres", "grant-api", "caddy")
            dc("up", "-d", "--wait", "--wait-timeout", "120", "postgres")
            backup()
            dc("stop", "caddy", "api")
            dc("run", "--rm", "--no-deps", "migrate")
            dc("run", "--rm", "--no-deps", "grant-api")
            dc("up", "-d", "--no-build", "--wait", "--wait-timeout", "180", "api", "caddy")
            dc("exec", "-T", "api", "wget", "-qO-", "http://127.0.0.1:8080/health/ready")
            print(f"\nAPI ready. Verify HTTPS: https://{values['PILOTFLOW_DOMAIN']}/health/ready")
        elif args.command == "backup":
            backup()
        elif args.command in ("restore-test", "restore-empty"):
            if args.file is None or not args.file.is_file():
                raise ValueError("Pass an existing .dump file")
            if args.command == "restore-test":
                restore_test(args.file.resolve())
                return
            dc("up", "-d", "--wait", "postgres")
            result = db("psql", "-X", "-U", "pilot_admin", "-d", "pilot_flow", "-Atc",
                        "SELECT count(*) FROM pg_namespace WHERE nspname IN ('pilot', 'pilot_migrations');",
                        capture_output=True, text=True)
            if result.stdout.strip() != "0":
                raise ValueError("Restore refused: database already has application schemas. Use a NEW server/volume.")
            dc("stop", "caddy", "api")
            with args.file.open("rb") as source:
                db("pg_restore", "-U", "pilot_admin", "-d", "pilot_flow", "--exit-on-error",
                   "--single-transaction", "--no-owner", "--no-privileges", stdin=source)
            dc("run", "--rm", "--no-deps", "grant-api")
            print("Backup restored. Run scripts/deploy.sh up to apply migrations and start the API.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"Deployment stopped: {error}", file=sys.stderr)
        sys.exit(1)
