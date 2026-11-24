#!/usr/bin/env python3
"""Get a short-lived token for the current manual frontend connection screen."""
import getpass
import json
import urllib.parse
import urllib.request

issuer = 'https://wooden-chrysopal29118.my-vm.work/auth/realms/pilot-flow'
username = input('Username [pilot-owner]: ').strip() or 'pilot-owner'
password = getpass.getpass('Password: ')
body = urllib.parse.urlencode({'grant_type': 'password', 'client_id': 'pilot-flow-api',
                              'username': username, 'password': password}).encode()
request = urllib.request.Request(issuer + '/protocol/openid-connect/token', data=body)
with urllib.request.urlopen(request, timeout=20) as response:
    print(json.load(response)['access_token'])
