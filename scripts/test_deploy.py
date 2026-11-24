import os
from pathlib import Path
import tempfile
import unittest

import deploy


class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.original = deploy.ENV
        deploy.ENV = Path(self.temp.name) / '.env'
        deploy.init()

    def tearDown(self):
        deploy.ENV = self.original
        self.temp.cleanup()

    def valid_values(self):
        values = deploy.read_env()
        values.update(PILOTFLOW_DOMAIN='api.pilotflow.test', ACME_EMAIL='admin@pilotflow.test',
                      PILOTFLOW_FRONTEND_ORIGIN='https://app.pilotflow.test',
                      PILOTFLOW_JWT_AUTHORITY='https://auth.pilotflow.test/realms/pilot-flow')
        return values

    def test_init_creates_private_distinct_passwords_and_refuses_overwrite(self):
        original = deploy.ENV.read_bytes()
        self.assertEqual(0o600, deploy.ENV.stat().st_mode & 0o777)
        values = deploy.read_env()
        self.assertNotEqual(values['PILOTFLOW_DB_PASSWORD'], values['PILOTFLOW_APP_DB_PASSWORD'])
        with self.assertRaises(FileExistsError):
            deploy.init()
        self.assertEqual(original, deploy.ENV.read_bytes())

    def test_unedited_template_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'example domain'):
            deploy.validate(deploy.read_env())

    def test_valid_configuration_is_accepted(self):
        deploy.validate(self.valid_values())

    def test_insecure_or_ambiguous_configuration_is_rejected(self):
        cases = [('PILOTFLOW_JWT_AUTHORITY', 'http://auth.pilotflow.test'),
                 ('PILOTFLOW_FRONTEND_ORIGIN', '*'),
                 ('PILOTFLOW_FRONTEND_ORIGIN', 'https://app.pilotflow.test/'),
                 ('PILOTFLOW_DOMAIN', 'https://api.pilotflow.test'),
                 ('PILOTFLOW_RELEASE', '../latest'),
                 ('PILOTFLOW_DB_PASSWORD', 'password')]
        for key, value in cases:
            with self.subTest(key=key, value=value):
                values = self.valid_values()
                values[key] = value
                with self.assertRaises(ValueError):
                    deploy.validate(values)

    def test_world_readable_secrets_are_rejected(self):
        deploy.ENV.chmod(0o644)
        with self.assertRaisesRegex(ValueError, 'chmod 600'):
            deploy.validate(self.valid_values())

    def test_environment_cannot_override_configured_password(self):
        os.environ['PILOTFLOW_DB_PASSWORD'] = 'unexpected'
        deploy.read_env()
        self.assertNotIn('PILOTFLOW_DB_PASSWORD', os.environ)

    def test_certificate_email_is_optional(self):
        values = self.valid_values()
        values["ACME_EMAIL"] = ""
        deploy.validate(values)
