"""Manda per email (Mailjet) il log dell'importatore con un riepilogo in cima.

Uso (nel workflow, dopo "Esegui importatore"): python .github/scripts/email_log.py <log> <appsettings.json>
Variabili d'ambiente: MAILJET_API_KEY, MAILJET_API_SECRET, ESITO (success/failure/...), RUN_URL, RUN_NUMBER.
Destinatari: Email:Recipients di appsettings.json (gli stessi dell'email degli eventi).
Non fa mai fallire il workflow: se qualcosa non va lo scrive e basta.
"""

import base64
import gzip
import json
import os
import re
import sys
import urllib.request
from datetime import datetime
from zoneinfo import ZoneInfo

MITTENTE = {"Email": "siballanewsletter@gmail.com", "Name": "siballanewsletter"}
MAX_ALLEGATO = 8 * 1024 * 1024  # oltre, il log viene compresso (.gz): Mailjet accetta al massimo 15 MB

# righe del log che finiscono nel riepilogo (prima occorrenza di ognuna, nell'ordine)
RIEPILOGO = [
    ("Impostazioni", r"Giorni = .*Radius = .*"),
    ("Righe nuove", r"\[AI\] Righe nuove per lo Sheet: .*"),
    ("Stili", r"\[stili\] .*"),
    ("Sheet", r"\[sheet\] '.*': \d+ righe 'da verificare' colorate.*"),
    ("Immagini", r"🖼️ Righe dello Sheet: .*"),
    ("Celle Immagine", r"\[sheet\] colonna 'Immagine' aggiornata: .*"),
    ("Email eventi", r"(📭|📬) .*"),
]


def main() -> None:
    log_path, settings_path = sys.argv[1], sys.argv[2]
    key, secret = os.environ.get("MAILJET_API_KEY"), os.environ.get("MAILJET_API_SECRET")
    if not key or not secret:
        print("Chiavi Mailjet mancanti: email del log non inviata")
        return

    with open(settings_path, encoding="utf-8-sig") as f:
        destinatari = [x.strip() for x in json.load(f)["Email"]["Recipients"].split(";") if x.strip()]
    if not destinatari:
        print("Nessun destinatario in Email:Recipients: email del log non inviata")
        return

    try:
        with open(log_path, "rb") as f:
            log = f.read()
    except FileNotFoundError:
        log = b"(log non trovato: il programma non e' partito, vedi il run su GitHub)"
    testo = log.decode("utf-8", errors="replace")
    righe = testo.splitlines()

    esito = os.environ.get("ESITO", "")
    ok = esito == "success"
    errori = [r for r in righe if "❌" in r or re.search(r"\b(Unhandled exception|Exception:)", r)]
    avvisi = [r for r in righe if "⚠️" in r]

    voci = []
    for nome, regex in RIEPILOGO:
        m = next((re.search(regex, r) for r in righe if re.search(regex, r)), None)
        if m:
            voci.append((nome, m.group(0).strip()))

    try:
        oggi = datetime.now(ZoneInfo("Europe/Rome")).strftime("%d/%m/%Y")
    except Exception:  # senza il database dei fusi orari: ora locale (il job ha già TZ=Europe/Rome)
        oggi = datetime.now().strftime("%d/%m/%Y")
    numero = os.environ.get("RUN_NUMBER", "")
    soggetto = f"{'✅' if ok else '❌'} Importatore #{numero} {oggi}" + ("" if ok else f" ({esito or 'errore'})") + \
        (f" · {len(errori)} errori" if errori else "")

    def esc(s: str) -> str:
        return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

    url = os.environ.get("RUN_URL", "")
    html = [f"<h2 style='margin:0 0 8px'>{esc(soggetto)}</h2>"]
    if url:
        html.append(f"<p><a href='{esc(url)}'>Apri il run su GitHub</a></p>")
    html.append("<table cellpadding='4' style='border-collapse:collapse;font-family:monospace;font-size:13px'>")
    for nome, valore in voci:
        html.append(f"<tr><td style='color:#666;white-space:nowrap'>{esc(nome)}</td><td>{esc(valore)}</td></tr>")
    html.append(f"<tr><td style='color:#666'>Errori</td><td>{len(errori)}</td></tr>")
    html.append(f"<tr><td style='color:#666'>Avvisi</td><td>{len(avvisi)}</td></tr></table>")
    for titolo, elenco in (("Errori", errori), ("Avvisi", avvisi)):
        if elenco:
            html.append(f"<h3>{titolo} (primi 30)</h3><pre style='font-size:12px;white-space:pre-wrap'>"
                        + esc("\n".join(elenco[:30])) + "</pre>")
    html.append("<p style='color:#666'>Log completo in allegato.</p>")

    nome_file, contenuto, tipo = "importatore.log.txt", log, "text/plain"
    if len(log) > MAX_ALLEGATO:
        nome_file, contenuto, tipo = "importatore.log.gz", gzip.compress(log), "application/gzip"

    messaggio = {
        "From": MITTENTE,
        "To": [{"Email": d} for d in destinatari],
        "Subject": soggetto,
        "TextPart": "\n".join(f"{n}: {v}" for n, v in voci) + f"\nErrori: {len(errori)}\nAvvisi: {len(avvisi)}\n{url}",
        "HTMLPart": "".join(html),
        "Attachments": [{"ContentType": tipo, "Filename": nome_file,
                         "Base64Content": base64.b64encode(contenuto).decode("ascii")}],
    }
    req = urllib.request.Request(
        "https://api.mailjet.com/v3.1/send",
        data=json.dumps({"Messages": [messaggio]}).encode("utf-8"),
        headers={"Content-Type": "application/json",
                 "Authorization": "Basic " + base64.b64encode(f"{key}:{secret}".encode()).decode()},
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            print(f"Email del log inviata a {', '.join(destinatari)} (HTTP {r.status}, allegato {len(contenuto) // 1024} KB)")
    except Exception as ex:  # l'email del log non deve far fallire il run
        print(f"Email del log non inviata: {ex}")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # mai far fallire il run per l'email del log
        print(f"Email del log non inviata: {ex}")
