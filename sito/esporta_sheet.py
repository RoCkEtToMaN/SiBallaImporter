"""Esporta le colonne A-N dello Google Sheet degli eventi in un JSON per il sito pubblico.

Uso: python sito/esporta_sheet.py <file di uscita>
Credenziali: variabile d'ambiente GOOGLE_SERVICE_ACCOUNT_JSON (contenuto del JSON del service account,
lo stesso secret usato dall'importatore).

Per ogni foglio dello Sheet:
- riga 2 = intestazioni, dati dalla riga 3 (come legge l'importatore);
- solo eventi futuri o in corso (data fine, o data inizio se manca, >= oggi in Italia);
- esclude le righe "da verificare" nella colonna "Luogo verificato" (come per il DB);
- pubblica solo le colonne A-N: le colonne dopo la N servono solo per il filtro e non finiscono nel JSON.
"""

import html
import json
import os
import re
import sys
from datetime import date, datetime, timedelta
from urllib.parse import quote
from zoneinfo import ZoneInfo

from google.auth.transport.requests import AuthorizedSession
from google.oauth2 import service_account

SPREADSHEET_ID = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo"
API = f"https://sheets.googleapis.com/v4/spreadsheets/{SPREADSHEET_ID}"
NUM_COLONNE = 14  # A..N
COL_LUOGO = 5  # F: "Paese, Città, Indirizzo(lat;lng)"
# colonne di A-N da non pubblicare (per nome dell'intestazione, minuscolo)
COLONNE_ESCLUSE = {"lindy", "blues", "boogie", "balboa"}
VERIFIED_HEADER = "luogo verificato"
VERIFIED_PENDING = "da verificare"
COORDINATE = re.compile(r"\(\s*(-?\d+(?:[.,]\d+)?)\s*;\s*(-?\d+(?:[.,]\d+)?)\s*\)")
EPOCA_SHEETS = date(1899, 12, 30)


def lettera(i: int) -> str:
    return chr(ord("A") + i)


def range_foglio(nome: str) -> str:
    return "'" + nome.replace("'", "''") + "'"


def come_data(valore) -> date | None:
    """Valore non formattato di una cella data: numero seriale di Sheets, oppure testo."""
    if valore is None or valore == "":
        return None
    if isinstance(valore, (int, float)):
        return EPOCA_SHEETS + timedelta(days=int(valore))
    testo = str(valore).strip()
    for formato in ("%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y", "%d.%m.%Y", "%m/%d/%Y"):
        try:
            return datetime.strptime(testo[:10], formato).date()
        except ValueError:
            pass
    return None


def cella(riga: list, i: int) -> str:
    return str(riga[i]).strip() if i < len(riga) and riga[i] is not None else ""


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit("Uso: python esporta_sheet.py <file di uscita>")
    chiave = os.environ.get("GOOGLE_SERVICE_ACCOUNT_JSON")
    if not chiave:
        sys.exit("Variabile GOOGLE_SERVICE_ACCOUNT_JSON mancante")

    credenziali = service_account.Credentials.from_service_account_info(
        json.loads(chiave), scopes=["https://www.googleapis.com/auth/spreadsheets.readonly"]
    )
    http = AuthorizedSession(credenziali)

    def get(url: str, **params) -> dict:
        r = http.get(url, params=params, timeout=60)
        r.raise_for_status()
        return r.json()

    fogli = [s["properties"]["title"] for s in get(API, fields="sheets.properties.title")["sheets"]]
    ranges = [range_foglio(f) for f in fogli]
    # valori come appaiono nello Sheet (per la pagina) e valori grezzi (per confrontare le date)
    formattati = get(f"{API}/values:batchGet", ranges=ranges, valueRenderOption="FORMATTED_VALUE")["valueRanges"]
    grezzi = get(
        f"{API}/values:batchGet", ranges=ranges,
        valueRenderOption="UNFORMATTED_VALUE", dateTimeRenderOption="SERIAL_NUMBER",
    )["valueRanges"]

    oggi = datetime.now(ZoneInfo("Europe/Rome")).date()
    uscita = {"aggiornato": datetime.now(ZoneInfo("Europe/Rome")).isoformat(timespec="minutes"), "fogli": []}

    for nome, vf, vg in zip(fogli, formattati, grezzi):
        righe_f = vf.get("values", [])
        righe_g = vg.get("values", [])
        if len(righe_f) < 3:
            print(f"- '{nome}': vuoto, saltato")
            continue

        intestazione = righe_f[1]
        colonne = [cella(intestazione, i) or lettera(i) for i in range(NUM_COLONNE)]
        col_verificato = next(
            (i for i, h in enumerate(intestazione) if str(h).strip().lower() == VERIFIED_HEADER), None
        )

        eventi = []
        scartate_passate = scartate_verifica = 0
        for n in range(2, len(righe_f)):
            rf = righe_f[n]
            rg = righe_g[n] if n < len(righe_g) else []
            if not any(cella(rf, i) for i in range(NUM_COLONNE)):
                continue
            inizio = come_data(rg[0] if len(rg) > 0 else None)
            fine = come_data(rg[1] if len(rg) > 1 else None) or inizio
            if fine is None or fine < oggi:
                scartate_passate += 1
                continue
            if col_verificato is not None and cella(rf, col_verificato).lower() == VERIFIED_PENDING:
                scartate_verifica += 1
                continue

            # html.unescape: alcuni nomi arrivano dai siti con entità tipo "&#8217;"
            celle = [html.unescape(cella(rf, i)) for i in range(NUM_COLONNE)]
            # date tutte nello stesso formato (nello Sheet ci sono sia 11/11/2026 sia 2026-11-11)
            if inizio:
                celle[0] = inizio.strftime("%d/%m/%Y")
            fine_scritta = come_data(rg[1] if len(rg) > 1 else None)
            if fine_scritta:
                celle[1] = fine_scritta.strftime("%d/%m/%Y")
            evento = {"inizio": inizio.isoformat() if inizio else fine.isoformat(), "celle": celle}
            # colonna F: tolgo le coordinate dal testo e le uso per il link alla mappa
            luogo = celle[COL_LUOGO]
            m = COORDINATE.search(luogo)
            if m:
                lat, lng = (float(x.replace(",", ".")) for x in m.groups())
                celle[COL_LUOGO] = luogo[: m.start()].strip().rstrip(",").strip()
                if lat or lng:
                    evento["mappa"] = f"https://www.google.com/maps/search/?api=1&query={lat},{lng}"
            elif luogo:
                evento["mappa"] = "https://www.google.com/maps/search/?api=1&query=" + quote(luogo)
            eventi.append(evento)

        # tolgo le colonne escluse (dopo l'uso degli indici fissi qui sopra; restano prima della F, quindi il
        # sito trova sempre inizio/fine/nome/luogo agli stessi indici)
        tenute = [i for i, c in enumerate(colonne) if c.strip().lower() not in COLONNE_ESCLUSE]
        colonne = [colonne[i] for i in tenute]
        for e in eventi:
            e["celle"] = [e["celle"][i] for i in tenute]

        eventi.sort(key=lambda e: e["inizio"])
        uscita["fogli"].append({"nome": nome, "colonne": colonne, "eventi": eventi})
        print(f"- '{nome}': {len(eventi)} eventi pubblicati, {scartate_passate} passati, {scartate_verifica} da verificare")

    os.makedirs(os.path.dirname(os.path.abspath(sys.argv[1])), exist_ok=True)
    with open(sys.argv[1], "w", encoding="utf-8") as f:
        json.dump(uscita, f, ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
