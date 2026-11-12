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

Le schede "Scuole", "Artisti" e "Community" non sono eventi: diventano scuole.json, artisti.json e
community.json accanto al file di uscita (per le pagine /scuole, /artisti, /community). Finché la scheda
non c'è si usa la bozza sito/elenchi/<nome>.csv. Gli artisti si pubblicano solo con Consenso = sì.
"""

import csv
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


# ---- pulizia dei nomi "sporchi" (solo quello che si vede sul sito: lo Sheet non cambia) ----
# Lettere "in grassetto" Unicode (𝗩𝗲𝗻𝗲𝗿𝗱𝗶 -> Venerdi), barre strane (┃ ⎜) -> "|", stelline decorative (☆ ★ ✰ ✧ ✮) -> "·",
# "-Italy" / ", Italy" in fondo, spazi multipli e a capo.
BARRE = re.compile(r"\s*[─-╿⎜-⎟｜∣ǀ]\s*")
STELLINE = re.compile(r"\s*[★☆✩-✰✴✶✷✸✹❂✪-✯✧✦⭐⯌]+\s*")
# solo con trattino o virgola: "Savoy Swing Italy" resta com'è
FINE_ITALY = re.compile(r"\s*[-–,]\s*Ital(?:y|ia)\s*$", re.I)


def pulisci_nome(s: str) -> str:
    if not s:
        return s
    t = unicodedata.normalize("NFKC", s)
    t = re.sub(r"\s*[\r\n]+\s*", " · ", t)
    t = BARRE.sub(" | ", t)
    t = STELLINE.sub(" · ", t)
    t = FINE_ITALY.sub("", t)
    t = re.sub(r"[ \t ]{2,}", " ", t)
    # separatori rimasti all'inizio o alla fine (es. "☆ Nome ☆" -> "Nome"), e doppi
    t = re.sub(r"(\s*[·|]\s*){2,}", " · ", t)
    t = re.sub(r"^[\s·|:–-]+|[\s·|:–-]+$", "", t)
    return t or s.strip()


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


# ---- elenchi per le pagine Scuole, Artisti, Community ----
# Schede dello Sheet con questi nomi non sono eventi: diventano <nome>.json accanto a eventi.json.
# Se la scheda non c'è ancora si usa la bozza sito/elenchi/<nome>.csv del repository (se c'è).
# "contenuti" = rubriche (mossa della settimana, giornate speciali, ...): colonne Data, Rubrica, Titolo, Testo,
# Immagine, Video, Autore; una riga compare sul sito dal giorno in "Data" in poi (così si programmano in anticipo).
ELENCHI = ("scuole", "artisti", "community", "contenuti")
CARTELLA_BOZZE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "elenchi")
CONSENSO_SI = {"sì", "si", "x", "yes", "true", "1", "ok"}

# regione di ogni evento (filtro "regione" e mappa della home): comune o provincia -> regione, dall'elenco ISTAT
# (sito/elenchi/comuni_regioni.json, nomi normalizzati con norm()). Eventi fuori dall'Italia: "Estero".
_REGIONI = None


def regione_di(luogo: str) -> str:
    global _REGIONI
    if _REGIONI is None:
        try:
            with open(os.path.join(CARTELLA_BOZZE, "comuni_regioni.json"), encoding="utf-8") as f:
                _REGIONI = json.load(f)
        except OSError:
            _REGIONI = {}
    parti = [p.strip() for p in luogo.split(",") if p.strip()]
    if not parti:
        return ""
    if not re.match(r"ital", parti[0], re.I):
        return "Estero"
    # prima la città (2° pezzo), poi gli altri pezzi dell'indirizzo (a volte la città è più avanti)
    for pezzo in parti[1:2] + parti[2:]:
        r = _REGIONI.get(norm(re.sub(r"\(.*?\)", "", pezzo)))
        if r:
            return r
    # frazione o località non in elenco: sigla della provincia tra parentesi, es. "Silvi Marina (TE)"
    m = re.search(r"\(([A-Za-z]{2})\)", luogo)
    return _REGIONI.get("sigla " + m.group(1).lower(), "") if m else ""


def chiave_colonna(h: str) -> str:
    """'Città' -> 'citta', 'Eventi nello Sheet' -> 'eventi_nello_sheet'."""
    return norm(h).replace(" ", "_")


def righe_elenco(nome: str, intestazione: list, righe: list) -> list[dict]:
    chiavi = [chiave_colonna(str(h)) for h in intestazione]
    out = []
    oggi = datetime.now(ZoneInfo("Europe/Rome")).date()
    for r in righe:
        d = {k: html.unescape(cella(r, i)) for i, k in enumerate(chiavi) if k and cella(r, i)}
        if nome == "contenuti":
            if not d.get("titolo"):
                continue
            giorno = come_data(d.get("data", ""))
            if giorno and giorno > oggi:
                continue   # programmato: compare dal suo giorno
            d["data"] = giorno.isoformat() if giorno else ""
            d["id"] = (d["data"] + "-" + norm(d["titolo"]).replace(" ", "-")).strip("-")[:80]
            out.append(d)
            continue
        if not d.get("nome"):
            continue
        # artisti: si pubblicano solo con il consenso (colonna "Consenso" = sì); senza la colonna, nessuno
        if nome == "artisti" and d.get("consenso", "").strip().lower() not in CONSENSO_SI:
            continue
        d.pop("consenso", None)
        out.append(d)
    if nome == "contenuti":
        out.sort(key=lambda c: c.get("data", ""), reverse=True)   # i più recenti prima
    return out


def scrivi_elenco(cartella: str, nome: str, righe: list[dict], fonte: str) -> None:
    with open(os.path.join(cartella, nome + ".json"), "w", encoding="utf-8") as f:
        json.dump({"aggiornato": datetime.now(ZoneInfo("Europe/Rome")).isoformat(timespec="minutes"),
                   "fonte": fonte, "righe": righe}, f, ensure_ascii=False, separators=(",", ":"))
    print(f"- elenco '{nome}': {len(righe)} righe (da {fonte})")


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

    cartella = os.path.dirname(os.path.abspath(sys.argv[1]))
    os.makedirs(cartella, exist_ok=True)
    elenchi_fatti = set()

    for nome, vf, vg in zip(fogli, formattati, grezzi):
        righe_f = vf.get("values", [])
        righe_g = vg.get("values", [])
        # Scuole / Artisti / Community / Contenuti: non sono eventi (riga 2 = intestazioni, dati dalla riga 3)
        if nome.strip().lower() in ELENCHI:
            elenco = nome.strip().lower()
            if len(righe_f) >= 2:
                scrivi_elenco(cartella, elenco, righe_elenco(elenco, righe_f[1], righe_f[2:]), "sheet")
                elenchi_fatti.add(elenco)
            continue
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
            # nome dell'evento (E) e organizzatore (G) puliti per la pagina; l'immagine usa già il nome originale qui sopra
            for k in (4, 6):
                if k < len(celle):
                    celle[k] = pulisci_nome(celle[k])
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
            reg = regione_di(celle[COL_LUOGO])
            if reg:
                evento["regione"] = reg
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

    # elenchi senza scheda nello Sheet: bozza CSV del repository, altrimenti vuoti
    for elenco in ELENCHI:
        if elenco in elenchi_fatti:
            continue
        bozza = os.path.join(CARTELLA_BOZZE, elenco + ".csv")
        if os.path.exists(bozza):
            with open(bozza, encoding="utf-8-sig", newline="") as f:
                tutte = list(csv.reader(f))
            scrivi_elenco(cartella, elenco, righe_elenco(elenco, tutte[0], tutte[1:]) if tutte else [], "bozza")
        else:
            scrivi_elenco(cartella, elenco, [], "nessuna")

    with open(sys.argv[1], "w", encoding="utf-8") as f:
        json.dump(uscita, f, ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
