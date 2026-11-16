"""Diagnostica in sola lettura per l'anteprima; non stampa credenziali o contatti."""
import json
import os
import re
import requests

account = os.environ['CLOUDFLARE_ACCOUNT_ID']
project = os.environ.get('PROGETTO', 'siballa')
base = 'https://api.cloudflare.com/client/v4/accounts/' + account
headers = {'Authorization': 'Bearer ' + os.environ['CLOUDFLARE_API_TOKEN']}

def api(method, path, **kwargs):
    r = requests.request(method, base + path, headers=headers, timeout=30, **kwargs)
    data = r.json()
    if not r.ok or not data.get('success'):
        print('Diagnostica API non disponibile: HTTP', r.status_code, 'codici', [e.get('code') for e in data.get('errors', [])])
        return None
    return data.get('result')

config = api('GET', '/pages/projects/' + project)
if config:
    configs = config.get('deployment_configs') or {}
    for ambiente in ['preview', 'production']:
        settings = configs.get(ambiente) or {}
        values = settings.get('env_vars') or {}
        flags = {name: bool(values.get(name)) for name in ['MAILJET_API_KEY', 'MAILJET_API_SECRET', 'EMAIL_DESTINATARI', 'STATS_KEY']}
        print(ambiente + ' configurazione:', json.dumps(flags))
    preview = configs.get('preview') or {}
    binding = (preview.get('d1_databases') or {}).get('VISITE') or {}
    database = binding.get('id') or binding.get('database_id')
    print('Preview binding VISITE presente:', bool(database))
    if database:
        data = api('POST', '/d1/database/' + database + '/query', json={'sql':'SELECT id, creato, stato, notifica FROM community_proposte ORDER BY id DESC LIMIT 10'})
        if data:
            for result in data:
                for row in result.get('results') or []:
                    row['notifica'] = re.sub(r'[^\s@]+@[^\s@]+', '[destinatario]', str(row.get('notifica', '')))
                    print('Richiesta:', json.dumps(row))
                if not result.get('results'):
                    print('Nessuna richiesta community presente nel DB di anteprima.')