import importlib.util
from pathlib import Path
import tarfile
import unittest

spec = importlib.util.spec_from_file_location('receiver', Path(__file__).with_name('receive-release.py'))
receiver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(receiver)

class ReleaseTests(unittest.TestCase):
    def test_regular_source_is_allowed(self):
        receiver.validate_members([tarfile.TarInfo('src/Program.cs')])

    def test_traversal_secrets_and_links_are_rejected(self):
        for name in ('../escape', '/etc/passwd', 'deploy/.env', 'deploy/auth.env', 'deploy/access.txt'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                receiver.validate_members([tarfile.TarInfo(name)])
        link = tarfile.TarInfo('link'); link.type = tarfile.SYMTYPE
        with self.assertRaises(ValueError):
            receiver.validate_members([link])
