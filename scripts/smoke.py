#!/usr/bin/env python3
"""Exercise the production containers locally in an isolated, disposable Compose project."""
import argparse
import json
from pathlib import Path
import secrets
import subprocess
import tempfile
import urllib.error
import urllib.request

import deploy


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    project = "pilot-flow-smoke-" + secrets.token_hex(4)
    with tempfile.TemporaryDirectory(prefix=".smoke-", dir=deploy.ROOT) as temp:
        folder = Path(temp)
        env = folder / ".env"
        config = (deploy.ROOT / "deploy/.env.example").read_text()
        for key in ("PILOTFLOW_DB_PASSWORD", "PILOTFLOW_APP_DB_PASSWORD"):
            config = config.replace(key + "=\n", key + "=" + secrets.token_hex(32) + "\n")
        env.write_text(config)
        env.chmod(0o600)
        deploy.ENV = env
        deploy.read_env()
        command = deploy.compose_command()
        command[command.index("--project-name") + 1] = project
        caddy = folder / "Caddyfile"
        caddy.write_text(":8080 {\n reverse_proxy api:8080\n}\n")
        override = folder / "smoke.yaml"
        override.write_text("services:\n  caddy:\n    ports: !override\n      - '127.0.0.1::8080'\n"
                            "    volumes: !override\n      - " + json.dumps(str(caddy) + ":/etc/caddy/Caddyfile:ro") + "\n")
        production = command[:]
        command += ["-f", str(override)]
        deploy.compose_command = lambda: command
        deploy.BACKUPS = folder / "backups"
        try:
            deploy.dc("config", "--quiet")
            if not args.no_build:
                deploy.dc("build", "api", "migrate")
            # Validate the real HTTPS configuration, without attempting certificate issuance.
            deploy.run(production + ["run", "--rm", "--no-deps", "caddy", "caddy", "validate", "--config", "/etc/caddy/Caddyfile"])
            deploy.dc("up", "-d", "--wait", "--wait-timeout", "120", "postgres")
            for _ in range(2):
                deploy.dc("run", "--rm", "--no-deps", "migrate")
                deploy.dc("run", "--rm", "--no-deps", "grant-api")
            deploy.dc("up", "-d", "--no-build", "--wait", "--wait-timeout", "180", "api", "caddy")
            address = deploy.dc("port", "caddy", "8080", capture_output=True, text=True).stdout.strip()
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            def request(path, method="GET", headers=None):
                req = urllib.request.Request("http://" + address + path, method=method, headers=headers or {})
                try:
                    return opener.open(req, timeout=10)
                except urllib.error.HTTPError as error:
                    return error
            for path in ("/health/live", "/health/ready", "/swagger/openapi.json"):
                with request(path) as response:
                    assert response.status == 200, (path, response.status)
            with request("/api/v1/me") as response:
                assert response.status == 401, response.status
                assert json.load(response)["code"] == "UNAUTHORIZED"
            with request("/api/v1/analyses", "OPTIONS", {
                "Origin": "https://app.example.com", "Access-Control-Request-Method": "PUT",
                "Access-Control-Request-Headers": "authorization,content-type,if-match"
            }) as response:
                assert response.status == 204, response.status
                assert response.headers["Access-Control-Allow-Origin"] == "https://app.example.com"
            # pg_dump + real restore, including migration history, not just an archive listing.
            dump = deploy.backup()
            deploy.restore_test(dump)
            roles = deploy.db("psql", "-X", "-U", "pilot_admin", "-d", "pilot_flow", "-Atc",
                              "SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname='pilot_app';",
                              capture_output=True, text=True).stdout.strip()
            assert roles == "f", "API database role has excessive permissions"
            deploy.dc("restart", "postgres", "api")
            deploy.dc("up", "-d", "--no-build", "--wait", "--wait-timeout", "180", "api", "caddy")
            with request("/health/ready") as response:
                assert response.status == 200, response.status
            print("SMOKE PASSED: production startup, repeat migrations, proxy, readiness, 401, CORS, backup/restore and restart.")
        finally:
            # Only this randomly named test project's temporary data is removed.
            deploy.dc("down", "--volumes", "--remove-orphans")


if __name__ == "__main__":
    main()
