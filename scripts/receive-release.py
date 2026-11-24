#!/usr/bin/env python3
"""Forced SSH command: receive SHA line and bounded source tarball from trusted CI."""
import fcntl
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile
import tempfile

BASE = Path('/opt/pilot-flow')
MAX_ARCHIVE = 25 * 1024 * 1024


def validate_members(members):
    for member in members:
        path = PurePosixPath(member.name)
        if path.is_absolute() or '..' in path.parts or member.issym() or member.islnk():
            raise ValueError('Unsafe archive path or link')
        if not (member.isfile() or member.isdir()):
            raise ValueError('Special files are forbidden')
        if any(part in {'.git', '.env', 'auth.env', 'auth-realm.json', 'access.txt', 'backups'} for part in path.parts):
            raise ValueError('Secrets and runtime data must not be in release')


def main():
    if os.geteuid() != 0:
        raise ValueError('Run through the restricted sudo command')
    sha = sys.stdin.buffer.readline(42).decode('ascii').strip()
    if not re.fullmatch('[0-9a-f]{40}', sha):
        raise ValueError('Expected a full Git commit SHA')
    with (BASE / 'deploy/.receive.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        with tempfile.TemporaryFile() as archive:
            size = 0
            while chunk := sys.stdin.buffer.read(65536):
                size += len(chunk)
                if size > MAX_ARCHIVE:
                    raise ValueError('Release archive too large')
                archive.write(chunk)
            archive.seek(0)
            releases = BASE / 'releases'
            releases.mkdir(exist_ok=True)
            # Unique attempt directory allows retrying the same commit without overwriting a live release.
            release = Path(tempfile.mkdtemp(prefix=sha + '-', dir=releases))
            with tarfile.open(fileobj=archive, mode='r:gz') as source:
                members = source.getmembers()
                validate_members(members)
                if sum(m.size for m in members) > 100 * 1024 * 1024:
                    raise ValueError('Unpacked release too large')
                source.extractall(release, members=members, filter='data')
        for required in ['scripts/deploy.py', 'compose.production.yaml', 'Dockerfile']:
            if not (release / required).is_file():
                raise ValueError('Missing release file: ' + required)
        env = BASE / 'deploy/.env'
        lines = env.read_text().splitlines()
        lines = [line for line in lines if not line.startswith('PILOTFLOW_RELEASE=')]
        env.write_text('\n'.join(lines) + '\nPILOTFLOW_RELEASE=' + sha + '\n')
        process_env = dict(os.environ, PILOTFLOW_ENV_FILE=str(env),
                           PILOTFLOW_BACKUP_DIR=str(BASE / 'backups'))
        subprocess.run(['python3', str(release / 'scripts/deploy.py'), 'up'],
                       env=process_env, check=True)
        link = BASE / 'current.next'
        link.symlink_to(release)
        link.replace(BASE / 'current')
        print('Deployed commit ' + sha)


if __name__ == '__main__':
    main()
