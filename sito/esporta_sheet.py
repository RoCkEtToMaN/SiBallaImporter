"""Esporta le colonne scelte dello Google Sheet degli eventi in un JSON per il sito pubblico.

Uso: python sito/esporta_sheet.py <file di uscita>
Credenziali: variabile d'ambiente GOOGLE_SERVICE_ACCOUNT_JSON (contenuto del JSON del service account,
lo stesso secret usato dall'importatore).

Per ogni foglio dello Sheet:
- riga 2 = intestazioni, dati dalla riga 3 (come legge l'importatore);
- solo eventi futuri o in corso (data fine, o data inizio se manca, >= oggi in Italia);
- esclude le righe "da verificare" nella colonna "Luogo verificato" (come per il DB);
- pubblica solo le colonne scelte (COLONNE_PER_FOGLIO / COLONNE_DEFAULT, meno COLONNE_ESCLUSE): le altre
  servono al massimo per il filtro e non finiscono nel JSON;
- aggiunge le coordinate (per "Vicino a me") e il link dell'immagine su Appwrite Storage se la colonna
  "Immagine" dice "sì" o "logo" (stesso id calcolato dall'importatore, vedi EventImages.cs).
"""

import hashlib
import html
import json
import os
import re
import sys
import unicodedata
from datetime import date, datetime, timedelta
from urllib.parse import quote
from zoneinfo import ZoneInfo

from google.auth.transport.requests import AuthorizedSession
from google.oauth2 import service_account

SPREADSHEET_ID = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo"
API = f"https://sheets.googleapis.com/v4/spreadsheets/{SPREADSHEET_ID}"
# colonne pubblicate: per foglio (nome in minuscolo), altrimenti COLONNE_DEFAULT.
# Devono comprendere A-F: il sito trova inizio, fine, nome e luogo in A, B, E, F.
COLONNE_DEFAULT = "A-N"
COLONNE_PER_FOGLIO = {"workshop": "A-H,R"}
COL_LUOGO = 5  # F: "Paese, Città, Indirizzo(lat;lng)"
# colonne da non pubblicare anche se comprese sopra (per nome dell'intestazione, minuscolo)
COLONNE_ESCLUSE = {"lindy", "blues", "boogie", "balboa"}
VERIFIED_HEADER = "luogo verificato"
VERIFIED_PENDING = "da verificare"
COORDINATE = re.compile(r"\(\s*(-?\d+(?:[.,]\d+)?)\s*;\s*(-?\d+(?:[.,]\d+)?)\s*\)")
EPOCA_SHEETS = date(1899, 12, 30)
IMAGE_HEADER = "immagine"
IMMAGINE_SI = {"sì", "si", "x", "yes", "true", "1"}
# link pubblico di un file del bucket "immagini" (AppWriteService.ImageViewUrl)
APPWRITE_VIEW = "https://cloud.appwrite.io/v1/storage/buckets/immagini/files/{}/view?project=6aa5651e00042e1452be"


# ---- stesse normalizzazioni dell'importatore (KnownVenueStore.Norm / NormalizeTitle, EventImages) ----

def norm(s: str) -> str:
    d = unicodedata.normalize("NFKD", html.unescape(s).strip().lower())
    out = "".join(c if ("a" <= c <= "z" or "0" <= c <= "9") else " "
                  for c in d if unicodedata.category(c) != "Mn")
    return re.sub(r"\s+", " ", out).strip()


def normalizza_titolo(titolo: str) -> str:
    if not titolo or not titolo.strip():
        return ""
    t = norm(titolo)
    t = re.sub(r"\b(19|20)\d{2}\b", " ", t)
    t = re.sub(r"\b\d+\s*(a|o)?\s*(edizione|edition|ed)\b", " ", t)
    t = re.sub(r"\b(edizione|edition|nd|rd|th)\b", " ", t)
    t = re.sub(r"\b\d+\b", " ", t)
    return re.sub(r"\s+", " ", t).strip()


def sha32(testo: str) -> str:
    return hashlib.sha256(testo.encode("utf-8")).hexdigest()[:32]


def url_immagine(valore: str, inizio: date | None, titolo: str, organizzatore: str, luogo: str) -> str | None:
    v = valore.strip().lower()
    if v in IMMAGINE_SI and inizio:
        return APPWRITE_VIEW.format("ev" + sha32(f"{inizio.isoformat()}|{normalizza_titolo(titolo)}"))
    if v == "logo":
        parti = luogo.split(",")
        citta = parti[1].split("(")[0].strip() if len(parti) > 1 else ""
        slug = (norm(organizzatore) + "|" + norm(citta)).replace(" ", "-").replace("|", "_")
        return APPWRITE_VIEW.format("lg" + sha32(slug))
    return None


def lettera(i: int) -> str:
    return chr(ord("A") + i)


def indici(spec: str) -> list[int]:
    """"A-H,R" -> [0..7, 17] (solo colonne A-Z)."""
    out = []
    for parte in spec.replace(" ", "").upper().split(","):
        da, _, a = parte.partition("-")
        out.extend(range(ord(da) - ord("A"), ord(a or da) - ord("A") + 1))
    return out


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
        scelte = indici(COLONNE_PER_FOGLIO.get(nome.strip().lower(), COLONNE_DEFAULT))
        num_colonne = max(scelte) + 1
        colonne = [cella(intestazione, i) or lettera(i) for i in range(num_colonne)]
        col_verificato = next(
            (i for i, h in enumerate(intestazione) if str(h).strip().lower() == VERIFIED_HEADER), None
        )
        col_immagine = next(
            (i for i, h in enumerate(intestazione) if str(h).strip().lower() == IMAGE_HEADER), None
        )

        eventi = []
        scartate_passate = scartate_verifica = 0
        for n in range(2, len(righe_f)):
            rf = righe_f[n]
            rg = righe_g[n] if n < len(righe_g) else []
            if not any(cella(rf, i) for i in scelte):
                continue
            inizio = come_data(rg[0] if len(rg) > 0 else None)
            fine = come_data(rg[1] if len(rg) > 1 else None) or inizio
            if fine is None or fine < oggi:
                scartate_passate += 1
                continue
            if col_verificato is not None and cella(rf, col_verificato).lower() == VERIFIED_PENDING:
                scartate_verifica += 1
                continue

            immagine = url_immagine(cella(rf, col_immagine), inizio, cella(rf, 4), cella(rf, 6), cella(rf, COL_LUOGO)) \
                if col_immagine is not None else None
            # html.unescape: alcuni nomi arrivano dai siti con entità tipo "&#8217;"
            celle = [html.unescape(cella(rf, i)) for i in range(num_colonne)]
            # date tutte nello stesso formato (nello Sheet ci sono sia 11/11/2026 sia 2026-11-11)
            if inizio:
                celle[0] = inizio.strftime("%d/%m/%Y")
            fine_scritta = come_data(rg[1] if len(rg) > 1 else None)
            if fine_scritta:
                celle[1] = fine_scritta.strftime("%d/%m/%Y")
            evento = {"inizio": inizio.isoformat() if inizio else fine.isoformat(), "celle": celle}
            if immagine:
                evento["img"] = immagine
            # colonna F: tolgo le coordinate dal testo e le uso per il link alla mappa
            luogo = celle[COL_LUOGO]
            m = COORDINATE.search(luogo)
            if m:
                lat, lng = (float(x.replace(",", ".")) for x in m.groups())
                celle[COL_LUOGO] = luogo[: m.start()].strip().rstrip(",").strip()
                if lat or lng:
                    evento["mappa"] = f"https://www.google.com/maps/search/?api=1&query={lat},{lng}"
                    evento["lat"], evento["lng"] = round(lat, 5), round(lng, 5)
            elif luogo:
                evento["mappa"] = "https://www.google.com/maps/search/?api=1&query=" + quote(luogo)
            eventi.append(evento)

        # tengo solo le colonne scelte e non escluse (dopo l'uso degli indici fissi qui sopra; A-F restano
        # sempre le prime, quindi il sito trova inizio/fine/nome/luogo agli stessi indici)
        tenute = [i for i in scelte if colonne[i].strip().lower() not in COLONNE_ESCLUSE]
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
