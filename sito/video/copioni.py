"""Copioni dei video how-to (pulsante "▶ Come funziona", sito/howto.js), uno per pagina.

Come rifarli (dal PC, Python con: pip install playwright imageio-ffmpeg tzdata google-auth requests;
poi: python -m playwright install chromium):
  1. copia del sito con i dati: copiare sito/*.html|css|js|svg|png in una cartella e lì dentro
     python sito/esporta_sheet.py <cartella>/eventi.json  (con GOOGLE_SERVICE_ACCOUNT_JSON e GITHUB_REF_NAME=anteprima,
     così escono anche scuole/musica/contenuti.json delle pagine non ancora pubbliche)
  2. python -m http.server 8765 --bind 127.0.0.1 --directory <cartella>
  3. cd sito/video && python copioni.py . [pagina ...]   (senza pagine: tutte)
Le chiamate /api in lettura vanno al sito pubblico, gli invii dei moduli sono bloccati (nessun dato vero).
"""
import sys
from howto import registra, LOCALE


def home(r):
    r.cap("Dove si balla stasera?", "Tutte le serate swing in Italia, in un posto solo", 3.2)
    r.scorri(320, 1.6)
    r.cap("Ogni evento ha data, luogo e stili", "Lindy Hop, Balboa, Shag, Blues…", 3)
    r.scorri(0, 1.2)
    r.cap("Cerca una città o un locale")
    r.dito("#q")
    r.digita("#q", "Milano")
    r.attendi(1.5)
    r.via()
    r.cap("La lista si filtra mentre scrivi", "", 2.4)
    r.svuota("#q")
    r.attendi(0.6)
    if r.c_e("#stili button[data-stile]:not(.tutti)"):
        r.cap("Scegli solo gli stili che balli")
        r.dito("#stili button[data-stile]:not(.tutti)")
        r.attendi(1.2)
        r.via()
        r.cap("Tocca di nuovo per toglierlo", "", 2)
        r.dito("#stili button[aria-pressed=true]")
        r.via()
    r.cap("📍 Vicino a me", "Mostra le serate più vicine a te")
    r.dito("#vicino")
    r.attendi(1.2)
    if r.pg.locator("#raggio").is_visible():
        r.cap("Scegli quanto sei disposto a viaggiare")
        r.dito("#raggio button[data-km='100']")
        r.attendi(1)
    r.via()
    r.cap("🗺️ Oppure guardale sulla mappa")
    r.dito("#btnMappa")
    r.via()
    r.attendi(1.2)
    r.scorri_a("#mappaBox", 20, 1)
    r.cap("Tocca un segnaposto per vedere la serata", "", 3)
    r.scorri(0, 1)
    r.dito("#btnMappa")
    r.via()
    if r.c_e("#lista .calendario"):
        r.cap("Ti piace una serata?", "Aggiungila al calendario o condividila con chi balla con te", 3.2)
        r.dito("#lista .calendario", clic=False, dopo=0.7)
        r.dito("#lista .condividi", clic=False, dopo=0.7)
        r.via()
    if r.pg.locator("#tabs button").count() > 1:
        r.cap("In alto trovi anche Workshop e Clandestini")
        r.dito("#tabs button[data-i='1']")
        r.attendi(1.5)
        r.via()
    r.finale()


def storia(r):
    r.cap("La storia del ballo", "Da Harlem negli anni '20 alle serate di oggi", 3)
    r.scorri_a(".capitolo", 20, 1.6)
    r.cap("Un capitolo per ogni epoca", "Il Savoy Ballroom, Frankie Manning, la rinascita…", 3)
    for i in range(1, 5):
        r.pg.evaluate("i => { const c = document.querySelectorAll('.capitolo')[i]; if (c) window.scrollTo({top: c.getBoundingClientRect().top + scrollY - 20, behavior: 'smooth'}); }", i)
        r.attendi(1.6)
        if i == 2:
            r.cap("Scorri per leggere tutta la storia", "", 2.4)
    r.scorri_a(".glossario", 20, 1.6)
    r.cap("In fondo, il glossario degli stili", "Lindy Hop, Balboa, Shag, Blues… spiegati in due righe", 3.4)
    r.scorri_di(500, 1.6)
    r.finale()


def rubriche(r):
    r.cap("Rubriche", "La mossa della settimana, le giornate speciali e altre storie", 3)
    r.scorri_a(".rubrica", 20, 1.2)
    r.cap("Le più recenti sono in alto", "", 2.4)
    if r.c_e(".rubrica .condividi"):
        r.cap("Condividila con chi balla con te")
        r.dito(".rubrica .condividi", clic=False, dopo=0.8)
        r.via()
    if r.c_e("#sondaggio"):
        r.scorri_a("#sondaggio", 20, 1.4)
        r.cap("Quali rubriche vorresti?", "Scegline quante vuoi: apriamo prima le più votate", 3)
        caselle = r.pg.locator("#opz input[type=checkbox]")
        for i in (0, 3):
            if caselle.count() > i:
                r.dito(caselle.nth(i).locator("xpath=.."), dopo=0.4)
        r.via()
        r.cap("Hai un'altra idea? Scrivila qui", "", 2)
        r.dito("#proposta", dopo=0.2)
        r.digita("#proposta", "Consigli per il primo festival")
        r.attendi(0.8)
        r.dito("#inviaS", clic=False)
        r.cap("Poi tocca Vota", "Puoi cambiare il voto quando vuoi", 2.8)
    r.finale()


def scuole(r):
    r.cap("Scuole di swing", "Dove imparare a ballare, in tutta Italia", 3)
    r.cap("Scegli la tua regione")
    r.dito(r.pg.locator("#contenuto button[data-scelta]", has_text="Emilia-Romagna"), dopo=1.2)
    r.via()
    r.scorri_di(300, 1.4)
    r.cap("Per ogni scuola: città, stili e contatti", "", 2.8)
    if r.c_e(".griglia a[href^='/?q=']"):
        r.cap("E i suoi prossimi eventi", "Toccali per vederli nell'elenco delle serate", 2.8)
        r.dito(".griglia a[href^='/?q=']", clic=False, dopo=0.8)
        r.via()
    r.scorri(0, 1)
    r.cap("Oppure cercala per nome o città")
    r.dito(r.pg.locator("#contenuto button[data-scelta]", has_text="Tutte"), dopo=0.5)
    r.dito("#filtro", dopo=0.2)
    r.digita("#filtro", "Milano")
    r.attendi(1.4)
    r.via()
    r.cap("La lista si filtra mentre scrivi", "", 2.4)
    r.finale()


def musica(r):
    r.cap("Musica per ballare", "Brani scelti per allenarsi e scoprire le band", 3)
    r.scorri_a(".tempi", 20, 1.4)
    r.cap("Le selezioni vanno per velocità", "Lente per iniziare, veloci per Balboa e Shag", 3.2)
    r.scorri_a("#filtri-musica", 20, 1.2)
    r.cap("Scegli il momento giusto")
    r.dito(r.pg.locator("#filtri-musica button", has_text="In pista"), dopo=1.2)
    r.via()
    r.scorri_a(".ascolta-tutta:visible", 160, 1.2)
    r.cap("▶ Ascolta la selezione", "O tocca un brano per sentire solo quello", 2.6)
    r.dito(".ascolta-tutta:visible", clic=False, dopo=0.6)
    r.dito(".brano:visible", clic=False, dopo=0.6)
    r.via()
    r.scorri_a("#filtri-musica", 20, 1)
    r.dito(r.pg.locator("#filtri-musica button", has_text="Tutta la musica"), dopo=0.6)
    r.via()
    r.cap("🔀 Oppure tutto in ordine casuale", "Come una radio swing", 2.4)
    r.dito("#tutto-casuale", clic=False, dopo=0.6)
    r.via()
    r.scorri_a("#proponi", 20, 1.4)
    r.cap("Conosci un brano perfetto?", "Proponilo: lo ascoltiamo e se va entra in una selezione", 3)
    r.dito("#pTitolo", dopo=0.2)
    r.digita("#pTitolo", "Jumpin' at the Woodside")
    r.dito("#pArtista", dopo=0.2)
    r.digita("#pArtista", "Count Basie")
    r.attendi(0.6)
    r.dito("#pInvia", clic=False)
    r.cap("Poi tocca Invia", "", 2.2)
    r.finale()


def anno2026(r):
    r.cap("Il tuo anno di swing", "Il 2026 di siBalla in numeri", 3)
    n = r.pg.locator(".slide").count()
    for i in range(1, n):
        r.togli_cap()
        r.pg.evaluate("i => window.scrollTo({top: document.querySelectorAll('.slide')[i].offsetTop, behavior: 'smooth'})", i)
        r.attendi(1.2)
        if i == 1:
            r.cap("Scorri per passare alla schermata dopo", "", 2.4)
        else:
            r.attendi(1.8)
    if r.c_e("#condividi"):
        r.cap("Alla fine, condividilo nelle storie", "", 2.6)
        r.dito("#condividi", clic=False, dopo=0.8)
        r.via()
    r.togli_cap()
    r.pg.evaluate("window.scrollTo({top: 0, behavior: 'smooth'})")
    r.attendi(1.2)
    r.cap("siballa.pages.dev", "Buone danze! 💃🕺", 3)


def app(r):
    r.cap("siBalla sul telefono", "Tutte le serate e i workshop in tasca", 3)
    r.scorri_a(".vantaggi", 20, 1.4)
    r.cap("Notifiche, Vicino a me, Preferiti", "Ti avvisa quando c'è una serata vicino a te", 3.2)
    r.cap("L'app arriva presto sugli store", "", 2.4)
    r.scorri_a(".passi", 20, 1.6)
    r.cap("Intanto: aggiungi il sito alla schermata Home", "Funziona come un'app, senza scaricare niente", 3.4)
    r.scorri_di(260, 1.2)
    r.cap("iPhone: Safari → Condividi → Aggiungi a Home", "Android: Chrome → ⋮ → Aggiungi a schermata Home", 4)
    r.finale()


def bacheca(r):
    r.cap("Bacheca", "Passaggi, partner di ballo, alloggio e mercatino", 3)
    r.cap("Filtra per tipo di annuncio")
    r.dito(r.pg.locator("#categorie button", has_text="Passaggi"), dopo=0.8)
    r.dito(r.pg.locator("#categorie button", has_text="Tutti"), dopo=0.3)
    r.via()
    r.cap("…o cerca per città, evento o stile")
    r.dito("#filtro", dopo=0.2)
    r.digita("#filtro", "Genova")
    r.attendi(1)
    r.svuota("#filtro")
    r.via()
    r.cap("Vuoi pubblicarne uno?")
    r.dito("#apri", dopo=0.8)
    r.cap("Scegli cosa cerchi o offri", "", 2)
    r.dito(r.pg.locator("#sCategoria button", has_text="Passaggio"), dopo=0.3)
    r.dito(r.pg.locator("#sTipo button", has_text="Offro"), dopo=0.3)
    r.cap("Da dove parti e dove vai", "", 1.6)
    r.dito("#fCitta", dopo=0.2)
    r.digita("#fCitta", "Genova")
    r.dito("#fArrivo", dopo=0.2)
    r.digita("#fArrivo", "Pescara")
    r.dito("#fTesto", dopo=0.2)
    r.digita("#fTesto", "3 posti in auto, partenza venerdì")
    r.via()
    r.cap("Lo controlliamo e in poche ore è online", "Chi è interessato ti risponde sotto l'annuncio", 3.2)
    r.dito("#annulla", dopo=0.8)
    r.via()
    r.finale()


def community(r):
    r.cap("Community WhatsApp", "Le chat swing della tua città", 3)
    r.scorri_a("#filtro", 120, 1.2)
    r.cap("Cerca la tua città o uno stile")
    r.dito("#filtro", dopo=0.2)
    r.digita("#filtro", "Genova")
    r.attendi(1.2)
    r.via()
    r.cap("Tocca \"Chiedi di entrare\"", "Un amministratore approva chi entra", 3)
    if r.c_e("a:has-text('Chiedi di entrare')"):
        r.dito("a:has-text('Chiedi di entrare')", clic=False, dopo=0.6)
        r.via()
    r.svuota("#filtro")
    r.attendi(0.6)
    if r.c_e("#segnalaCommunity"):
        r.cap("La tua città non c'è? Fondala tu", "Oppure segnalaci un gruppo che esiste già", 3)
        r.dito("#segnalaCommunity", dopo=0.8)
        r.dito("#cCitta", dopo=0.2)
        r.digita("#cCitta", "Pescara")
        r.dito("#cTitolo", dopo=0.2)
        r.digita("#cTitolo", "Swing Pescara")
        r.via()
        r.cap("Lo valutiamo prima di aggiungerlo", "I link d'invito non vengono pubblicati liberi", 3)
        r.dito("#chiudiCommunity", dopo=0.6)
        r.via()
    r.finale()


def novita(r):
    r.cap("Novità", "Cosa c'è di nuovo su siBalla", 3)
    r.scorri_di(300, 1.4)
    r.cap("Ogni versione con le sue novità", "La più recente è in alto", 3)
    r.scorri_di(600, 1.6)
    r.togli_cap()
    r.scorri_di(600, 1.6)
    r.pg.evaluate("window.scrollTo({top: document.documentElement.scrollHeight, behavior: 'smooth'})")
    r.attendi(1.6)
    r.cap("Da qui torni agli eventi in un tocco", "", 2, su=True)
    if r.c_e("a.torna"):
        r.dito("a.torna", clic=False, dopo=1)
    r.finale()


PAGINE = {
    "home": (home, "index.html", "#lista > *"),
    "storia": (storia, "storia.html", None),
    "rubriche": (rubriche, "rubriche.html", ".rubrica"),
    "scuole": (scuole, "scuole.html", "#filtro"),
    "musica": (musica, "musica.html", ".ascolta-tutta"),
    "2026": (anno2026, "2026.html", ".slide"),
    "app": (app, "app.html", None),
    "bacheca": (bacheca, "bacheca.html", "#categorie button"),
    "community": (community, "community.html", "#filtro"),
    "novita": (novita, "novita.html", "#novita > *"),
}

if __name__ == "__main__":
    out = sys.argv[1]
    for nome in sys.argv[2:] or PAGINE:
        f, url, sel = PAGINE[nome]
        try:
            registra(nome, f"{LOCALE}/{url}", f, out, sel, geo=(nome == "home"))
        except Exception as e:
            print(f"{nome}: ERRORE {e}", flush=True)
