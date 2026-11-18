"""Numeri dell'anno per la pagina /2026 ("siBalla 2026 in numeri"), calcolati dallo Google Sheet.

Uso: python sito/numeri_anno.py <file di uscita> [anno]
     (anno di default: quello in corso in Italia; credenziali come esporta_sheet.py:
      variabile GOOGLE_SERVICE_ACCOUNT_JSON, oppure GOOGLE_SERVICE_ACCOUNT_FILE = percorso del JSON)

Lo scrive il workflow del sito accanto a eventi.json (numeri-<anno>.json); la pagina lo legge al caricamento.
I numeri del sito (visitatori, pagine viste, annunci, clandestini) arrivano invece da /api/numeri (database D1).

Regole:
- schede Serate e Workshop (le "Backup ..." no), righe con l'inizio nell'anno;
- si tolgono le righe scartate ("no") e quelle "da verificare" (altri balli o mai controllate), come per il sito;
- solo eventi in Italia (i workshop sono in gran parte festival all'estero: li conta a parte, "estero");
- doppioni (stesso giorno e stesso nome, a meno di spazi, punteggiatura e numeri) contati una volta;
- festival = workshop di almeno 2 giorni (nello Sheet "Fine" è il giorno dopo l'ultimo: una serata
  va dall'11/08 al 12/08, quindi la durata è fine - inizio);
- stili = colonne Lindy, Blues, ... con "X" (la colonna "Stile" è una formula che le riassume), in % sul totale
  delle indicazioni di stile;
- lo Sheet fa da storico solo dall'agosto 2026 (vedi CLAUDE.md): "daMese" dice da quale mese ci sono dati,
  la pagina mostra solo i mesi da lì in poi.
"""

import json
import os
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime
from zoneinfo import ZoneInfo

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import esporta_sheet as es  # noqa: E402

SCHEDE = {"serate": "serate", "workshop": "workshop"}
STATI_FUORI = {es.VERIFIED_PENDING, es.VERIFIED_NO}
STILI = {  # intestazione della colonna -> nome mostrato
    "lindy": "Lindy Hop", "balboa": "Balboa", "blues": "Blues", "boogie": "Boogie Woogie",
    "shag": "Shag", "jazz": "Solo jazz", "aerials": "Aerials", "afrodance": "Afrodance",
}
QUANTI_CITTA = 5
QUANTI_STILI = 5


def chiave_nome(nome: str) -> str:
    """'WeBalParis 2026' e 'We Bal Paris' -> 'webalparis' (solo lettere)."""
    return re.sub(r"[^a-z]", "", es.norm(es.pulisci_nome(nome)))


def citta_di(luogo: str) -> str:
    """'Italia, Brescia(45,891;10,18658)' -> 'Brescia'; 'Italia, Provincia di Lecce' -> 'Lecce'."""
    senza = es.COORDINATE.sub("", luogo)
    parti = [p.strip() for p in senza.split(",")]
    if len(parti) < 2:
        return ""
    c = re.sub(r"\(.*?\)", "", parti[1]).strip()
    c = re.sub(r"^(provincia|citt[aà] metropolitana) di\s+", "", c, flags=re.I)
    return re.sub(r"[<>&\"]", "", c)  # la pagina lo mette nell'HTML così com'è


def calcola(schede: dict[str, tuple[list, list]], anno: int) -> dict:
    """schede: nome in minuscolo -> (righe formattate, righe grezze), come le dà la Sheets API (riga 2 = intestazioni)."""
    visti = set()
    conteggio = {"serate": 0, "workshop": 0}
    festival = estero = 0
    mesi = [0] * 12
    citta = Counter()
    grafie = defaultdict(Counter)  # stessa città scritta in modi diversi: si mostra la grafia più usata
    regioni = set()
    organizzatori = set()
    stili = Counter()
    primo = None

    for nome, (righe_f, righe_g) in schede.items():
        tipo = SCHEDE.get(nome)
        if not tipo or len(righe_f) < 3:
            continue
        intest = [str(h).strip().lower() for h in righe_f[1]]
        col_ver = intest.index(es.VERIFIED_HEADER) if es.VERIFIED_HEADER in intest else None
        col_stili = {i: STILI[h] for i, h in enumerate(intest) if h in STILI}
        for n in range(2, len(righe_f)):
            rf = righe_f[n]
            rg = righe_g[n] if n < len(righe_g) else []
            inizio = es.come_data(rg[0] if rg else None)
            if not inizio or inizio.year != anno:
                continue
            if col_ver is not None and es.cella(rf, col_ver).lower() in STATI_FUORI:
                continue
            nome_ev, luogo = es.cella(rf, 4), es.cella(rf, es.COL_LUOGO)
            if not nome_ev:
                continue
            chiave = (inizio, chiave_nome(nome_ev))
            if chiave in visti:
                continue
            visti.add(chiave)
            if not re.match(r"\s*ital", luogo, re.I):
                estero += 1
                continue

            primo = min(primo, inizio) if primo else inizio
            conteggio[tipo] += 1
            fine = es.come_data(rg[1] if len(rg) > 1 else None)
            if tipo == "workshop" and fine and (fine - inizio).days >= 2:
                festival += 1
            mesi[inizio.month - 1] += 1
            c = citta_di(luogo)
            if c:
                k = es.norm(c)
                citta[k] += 1
                grafie[k][c] += 1
            reg = es.regione_di(es.COORDINATE.sub("", luogo))
            if reg and reg != "Estero":
                regioni.add(reg)
            org = chiave_nome(es.cella(rf, 6))
            if org:
                organizzatori.add(org)
            for i, s in col_stili.items():
                if es.cella(rf, i).upper() == "X":
                    stili[s] += 1

    tot_stili = sum(stili.values()) or 1
    return {
        "anno": anno,
        "daMese": primo.month if primo else 1,
        "aggiornato": datetime.now(ZoneInfo("Europe/Rome")).isoformat(timespec="minutes"),
        "serate": conteggio["serate"],
        "workshop": conteggio["workshop"],
        "festival": festival,
        "estero": estero,
        "citta": len(citta),
        "regioni": len(regioni),
        "organizzatori": len(organizzatori),
        "classificaCitta": [[grafie[k].most_common(1)[0][0], v] for k, v in citta.most_common(QUANTI_CITTA)],
        "mesi": mesi,
        "stili": [[s, round(v * 100 / tot_stili)] for s, v in stili.most_common(QUANTI_STILI)],
    }


def leggi_schede() -> dict[str, tuple[list, list]]:
    from google.auth.transport.requests import AuthorizedSession
    from google.oauth2 import service_account

    scope = ["https://www.googleapis.com/auth/spreadsheets.readonly"]
    if os.environ.get("GOOGLE_SERVICE_ACCOUNT_JSON"):
        cred = service_account.Credentials.from_service_account_info(
            json.loads(os.environ["GOOGLE_SERVICE_ACCOUNT_JSON"]), scopes=scope)
    elif os.environ.get("GOOGLE_SERVICE_ACCOUNT_FILE"):
        cred = service_account.Credentials.from_service_account_file(os.environ["GOOGLE_SERVICE_ACCOUNT_FILE"], scopes=scope)
    else:
        sys.exit("Variabile GOOGLE_SERVICE_ACCOUNT_JSON (o GOOGLE_SERVICE_ACCOUNT_FILE) mancante")
    http = AuthorizedSession(cred)
    fogli = [s["properties"]["title"] for s in
             http.get(es.API, params={"fields": "sheets.properties.title"}, timeout=60).json()["sheets"]]
    fogli = [f for f in fogli if f.strip().lower() in SCHEDE]
    ranges = [es.range_foglio(f) for f in fogli]

    def batch(**opz) -> list:
        r = http.get(f"{es.API}/values:batchGet", params={"ranges": ranges, **opz}, timeout=60)
        r.raise_for_status()
        return [v.get("values", []) for v in r.json()["valueRanges"]]

    formattati = batch(valueRenderOption="FORMATTED_VALUE")
    grezzi = batch(valueRenderOption="UNFORMATTED_VALUE", dateTimeRenderOption="SERIAL_NUMBER")
    return {f.strip().lower(): (vf, vg) for f, vf, vg in zip(fogli, formattati, grezzi)}


def main() -> None:
    if len(sys.argv) not in (2, 3):
        sys.exit("Uso: python numeri_anno.py <file di uscita> [anno]")
    anno = int(sys.argv[2]) if len(sys.argv) == 3 else datetime.now(ZoneInfo("Europe/Rome")).year
    dati = calcola(leggi_schede(), anno)
    os.makedirs(os.path.dirname(os.path.abspath(sys.argv[1])), exist_ok=True)
    with open(sys.argv[1], "w", encoding="utf-8") as f:
        json.dump(dati, f, ensure_ascii=False, separators=(",", ":"))
    print(f"- numeri {anno} (da mese {dati['daMese']}): {dati['serate']} serate, {dati['workshop']} workshop "
          f"({dati['festival']} festival), {dati['citta']} città, {dati['regioni']} regioni, {dati['estero']} all'estero")


if __name__ == "__main__":
    main()
