"""Reels/Storie per Instagram (1080x1920): apertura con il logo, registrazione del sito, chiusura con l'indirizzo.

Stessa preparazione di copioni.py (copia locale del sito servita su 127.0.0.1:8765), poi:
  cd sito/video && python social.py <cartella_uscita> [home bacheca community]
Escono reel-<nome>.mp4 (con traccia audio muta: la musica si sceglie in Instagram) e reel-<nome>-copertina.jpg.
Le didascalie stanno fuori dalle fasce che Instagram copre (circa 15% in alto e 20% in basso).
"""
import os, sys, subprocess, base64
import imageio_ffmpeg
from playwright.sync_api import sync_playwright
from howto import registra, LOCALE

QUI = os.path.dirname(os.path.abspath(__file__))
LOGO = os.path.join(QUI, "..", "logo-app.svg")

CARTA = """<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;width:360px;height:640px;background:radial-gradient(120% 80% at 50% 30%,#2a2208 0%,#0d0d12 60%);
  color:#fff;font-family:system-ui,-apple-system,Segoe UI,sans-serif;overflow:hidden}
.box{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;
  text-align:center;padding:0 30px;gap:18px}
.logo{width:120px;height:120px;border-radius:28px;background:#fff;display:flex;align-items:center;justify-content:center;
  box-shadow:0 10px 40px rgba(255,212,0,.25)}
.logo img{width:96px}
.brand{font-weight:800;font-size:22px;line-height:1.15}.brand span{color:#FFD400}
.titolo{font-weight:800;font-size:{dim}px;line-height:1.15}
.sotto{font-size:18px;opacity:.85;line-height:1.35}
.url{font-weight:800;font-size:30px;color:#FFD400}
</style></head><body><div class="box">
<div class="logo"><img src="data:image/svg+xml;base64,{logo}"></div>
<div class="brand">Dove <span>siBalla</span> stasera?</div>
<div class="titolo">{titolo}</div><div class="sotto">{sotto}</div>{url}
</div></body></html>"""


def carta(pg, percorso, titolo, sotto="", url=False, dim=34):
    with open(LOGO, "rb") as f:
        logo = base64.b64encode(f.read()).decode()
    html = (CARTA.replace("{logo}", logo).replace("{titolo}", titolo).replace("{sotto}", sotto)
            .replace("{dim}", str(dim)).replace("{url}", '<div class="url">siballa.pages.dev</div>' if url else ""))
    pg.set_content(html)
    pg.wait_for_timeout(300)
    pg.screenshot(path=percorso)


# ---- copioni brevi (15-30 secondi), senza chiusura: la fa la carta finale ----

def home(r):
    r.cap("Tutte le serate swing d'Italia", "in un posto solo", 2.4)
    r.cap("Cerca la tua città", "", 1.4)
    r.dito("#q", dopo=0.2)
    r.digita("#q", "Milano")
    r.attendi(1.2)
    r.via()
    r.svuota("#q")
    r.cap("📍 …o quelle vicino a te", "", 1.4)
    r.dito("#vicino", dopo=1.2)
    r.via()
    r.cap("🗺️ Anche sulla mappa", "", 1.2)
    r.dito("#btnMappa", dopo=0.4)
    r.via()
    r.attendi(0.8)
    r.scorri_a("#mappaBox", 20, 2.2)
    r.togli_cap()
    if r.c_e("#lista .calendario"):
        r.scorri_a("#lista .calendario", 260, 1.2)
        r.cap("📅 Salvala nel calendario", "e mandala a chi balla con te", 2)
        r.dito("#lista .calendario", clic=False, dopo=0.4)
        r.dito("#lista .condividi", clic=False, dopo=0.8)
        r.via()


def bacheca(r):
    r.cap("Sulla Bacheca di siBalla", "passaggi, partner di ballo, alloggio, mercatino", 2.6)
    r.dito("#apri", dopo=0.6)
    r.cap("Scrivi cosa cerchi o cosa offri", "", 1.6)
    r.dito(r.pg.locator("#sCategoria button", has_text="Passaggio"), dopo=0.2)
    r.dito(r.pg.locator("#sTipo button", has_text="Offro"), dopo=0.2)
    r.cap("Da dove parti, dove vai", "", 0.8, su=True)
    r.dito("#fCitta", dopo=0.1)
    r.digita("#fCitta", "Genova")
    r.dito("#fArrivo", dopo=0.1)
    r.digita("#fArrivo", "Pescara")
    r.via()
    r.cap("Chi è interessato ti risponde", "direttamente sotto l'annuncio", 2.6, su=True)
    r.dito("#annulla", dopo=0.6)
    r.via()


def community(r):
    r.pg.evaluate("() => { const e = document.querySelector('#filtro'); window.scrollTo(0, e.getBoundingClientRect().top + scrollY - 140); }")
    r.attendi(0.3)
    r.cap("Le chat WhatsApp swing", "città per città", 2.2)
    r.dito("#filtro", dopo=0.1)
    r.digita("#filtro", "Genova")
    r.attendi(0.8)
    r.via()
    if r.c_e("a:has-text('Chiedi di entrare')"):
        r.cap("Chiedi di entrare", "un amministratore ti aggiunge", 1.6)
        r.dito("a:has-text('Chiedi di entrare')", clic=False, dopo=0.8)
        r.via()
    r.svuota("#filtro")
    r.attendi(0.4)
    if r.c_e("#segnalaCommunity"):
        r.scorri_a("#segnalaCommunity", 300, 1.2)
        r.cap("La tua città non c'è?", "Fondala tu: te la aggiungiamo noi", 2)
        r.dito("#segnalaCommunity", clic=False, dopo=0.8)
        r.via()


REEL = {
    # nome: (copione, pagina, attesa, geo, titolo apertura, sotto apertura)
    "home": (home, "index.html", "#lista > *", True, "Dove si balla swing stasera?", "Serate e workshop in tutta Italia"),
    "bacheca": (bacheca, "bacheca.html", "#categorie button", False, "Cerchi un passaggio per il festival?", "O un partner, un letto, un paio di scarpe"),
    "community": (community, "community.html", "#filtro", False, "La chat swing della tua città", "Serate dell'ultimo minuto e nuovi amici di pista"),
}


def monta(ff, intro, video, outro, uscita):
    filtro = ("[0:v]scale=1080:1920:out_range=tv,setsar=1,fps=30,format=yuv420p,fade=t=in:st=0:d=0.3,fade=t=out:st=1.9:d=0.3[a];"
              "[1:v]scale=1080:1920:out_range=tv,setsar=1,fps=30,format=yuv420p[b];"
              "[2:v]scale=1080:1920:out_range=tv,setsar=1,fps=30,format=yuv420p,fade=t=in:st=0:d=0.3[c];"
              "[a][b][c]concat=n=3:v=1:a=0[v]")
    subprocess.run([ff, "-y", "-loglevel", "error", "-loop", "1", "-t", "2.2", "-i", intro, "-i", video,
                    "-loop", "1", "-t", "2.8", "-i", outro, "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
                    "-filter_complex", filtro, "-map", "[v]", "-map", "3:a", "-shortest",
                    "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-profile:v", "high", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", uscita], check=True)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    for nome in sys.argv[2:] or REEL:
        copione, pagina, attesa, geo, titolo, sotto = REEL[nome]
        grezzo = registra("reel-" + nome, f"{LOCALE}/{pagina}", copione, out, attesa, geo=geo, formato="social")
        intro = os.path.join(out, f"_intro-{nome}.png")
        outro = os.path.join(out, "_outro.png")
        with sync_playwright() as p:
            b = p.chromium.launch()
            pg = b.new_page(viewport={"width": 360, "height": 640}, device_scale_factor=3)
            carta(pg, intro, titolo, sotto)
            carta(pg, outro, "Tutte le serate swing d'Italia", "Buone danze! 💃🕺", url=True, dim=26)
            b.close()
        finale = os.path.join(out, f"reel-{nome}.mp4")
        monta(ff, intro, grezzo, outro, finale)
        subprocess.run([ff, "-y", "-loglevel", "error", "-i", intro, "-q:v", "3",
                        os.path.join(out, f"reel-{nome}-copertina.jpg")], check=True)
        for f in (grezzo, grezzo[:-4] + ".jpg", intro):
            if os.path.exists(f):
                os.remove(f)
        print(f"reel-{nome}.mp4 pronto ({os.path.getsize(finale) / 1024 / 1024:.1f} MB)", flush=True)
