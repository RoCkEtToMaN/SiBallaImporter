"""Motore comune per i video how-to di siBalla: browser a misura di telefono, didascalie e "dito" disegnati nella
pagina, fotogrammi catturati a 2x via CDP e montati in MP4 con ffmpeg."""
import os, time, base64, shutil, subprocess
import imageio_ffmpeg
from playwright.sync_api import sync_playwright

W, H = 390, 760
# formati: (larghezza, altezza, scala) -> video di larghezza*scala x altezza*scala
FORMATI = {"sito": (390, 760, 2), "social": (360, 640, 3)}   # social = 1080x1920 per Reels e Storie
# Reels/Storie: Instagram copre ~15% in alto e ~20% in basso, la didascalia sta sopra quella fascia
CSS_SOCIAL = "#hw-cap{bottom:24%;font-size:23px;padding:16px 18px} #hw-cap small{font-size:17px} #hw-cap.su{top:16%}"
LOCALE = "http://127.0.0.1:8765"
PUBBLICO = "https://siballa.pages.dev"

OVERLAY = r"""
(() => {
  if (document.getElementById('hw-cap')) return;
  const st = document.createElement('style');
  st.textContent = `
  #hw-cap{position:fixed;left:14px;right:14px;bottom:22px;z-index:2147483646;padding:14px 16px;border-radius:16px;
    background:rgba(20,16,40,.92);color:#fff;font:600 19px/1.3 system-ui,-apple-system,Segoe UI,sans-serif;
    text-align:center;box-shadow:0 8px 30px rgba(0,0,0,.35);opacity:0;transform:translateY(12px);
    transition:opacity .35s,transform .35s;pointer-events:none}
  #hw-cap.on{opacity:1;transform:none}
  #hw-cap.su{top:22px;bottom:auto}
  #hw-cap small{display:block;font-weight:400;font-size:15px;opacity:.8;margin-top:4px}
  #hw-dito{position:fixed;z-index:2147483647;width:44px;height:44px;margin:-22px 0 0 -22px;border-radius:50%;
    background:rgba(255,190,40,.55);border:3px solid rgba(255,255,255,.95);box-shadow:0 2px 10px rgba(0,0,0,.35);
    left:50%;top:110%;transition:left .7s ease-in-out,top .7s ease-in-out,transform .15s;pointer-events:none}
  #hw-dito.giu{transform:scale(.7)}
  #hwBtn{display:none!important}`;
  document.head.appendChild(st);
  const c = document.createElement('div'); c.id = 'hw-cap'; document.body.appendChild(c);
  const d = document.createElement('div'); d.id = 'hw-dito'; document.body.appendChild(d);
})()"""


def _api(route):
    """In locale le funzioni Cloudflare non ci sono: le letture vanno al sito pubblico, gli invii non partono mai."""
    r = route.request
    if r.method != "GET":
        return route.fulfill(status=200, content_type="application/json", body='{"ok":true}')
    try:
        resp = route.fetch(url=r.url.replace(LOCALE, PUBBLICO))
        if "json" in resp.headers.get("content-type", ""):
            return route.fulfill(response=resp)
    except Exception:
        pass
    route.fulfill(status=200, content_type="application/json", body='{"righe":[],"esclusi":[],"annunci":[]}')


class Regia:
    def __init__(self, pg):
        self.pg = pg

    def overlay(self):
        self.pg.evaluate(OVERLAY)

    def ospita(self):
        """Le finestre aperte con showModal stanno sopra a tutto: didascalia e dito vanno dentro la finestra."""
        self.pg.evaluate("""() => { const h = document.querySelector('dialog[open]') || document.body;
            for (const id of ['hw-cap','hw-dito']) { const e = document.getElementById(id); if (e && e.parentNode !== h) h.appendChild(e); } }""")

    def cap(self, testo, sotto="", attesa=2.6, su=False):
        self.ospita()
        self.pg.evaluate("""([t,s,su]) => { const c=document.getElementById('hw-cap'); c.classList.remove('on');
            setTimeout(()=>{ c.innerHTML = t + (s ? '<small>'+s+'</small>' : ''); c.classList.toggle('su', su);
            c.classList.add('on'); }, 250); }""", [testo, sotto, su])
        self.attendi(attesa)

    def togli_cap(self):
        self.pg.evaluate("document.getElementById('hw-cap').classList.remove('on')")

    def attendi(self, s):
        self.pg.wait_for_timeout(int(s * 1000))

    def dito(self, sel, clic=True, dopo=0.5):
        el = self.pg.locator(sel).first if isinstance(sel, str) else sel
        self.ospita()
        el.scroll_into_view_if_needed()
        self.attendi(0.3)
        b = el.bounding_box()
        x, y = b["x"] + b["width"] / 2, b["y"] + b["height"] / 2
        self.pg.evaluate("([x,y]) => { const d=document.getElementById('hw-dito'); d.style.left=x+'px'; d.style.top=y+'px'; }", [x, y])
        self.attendi(0.9)
        if clic:
            self.pg.evaluate("document.getElementById('hw-dito').classList.add('giu')")
            self.attendi(0.18)
            el.click()
            self.pg.evaluate("document.getElementById('hw-dito').classList.remove('giu')")
        self.attendi(dopo)

    def via(self):
        self.pg.evaluate("document.getElementById('hw-dito').style.top='110%'")

    def digita(self, sel, testo):
        self.pg.locator(sel).first.press_sequentially(testo, delay=130)

    def svuota(self, sel):
        loc = self.pg.locator(sel).first
        loc.fill("")
        loc.dispatch_event("input")

    def scorri(self, y, attesa=1.4):
        self.pg.evaluate("y => window.scrollTo({top:y, behavior:'smooth'})", y)
        self.attendi(attesa)

    def scorri_a(self, sel, margine=90, attesa=1.4):
        self.pg.evaluate("""([s,m]) => { const e=[...document.querySelectorAll(s.replace(":visible",""))].find(x => x.offsetParent); if(!e) return;
            window.scrollTo({top: e.getBoundingClientRect().top + window.scrollY - m, behavior:'smooth'}); }""", [sel, margine])
        self.attendi(attesa)

    def scorri_di(self, dy, attesa=1.4):
        self.pg.evaluate("y => window.scrollBy({top:y, behavior:'smooth'})", dy)
        self.attendi(attesa)

    def c_e(self, sel):
        return self.pg.locator(sel).count() > 0

    def finale(self):
        self.via()
        self.togli_cap()
        self.attendi(0.3)
        self.scorri(0, 1)
        self.cap("siballa.pages.dev", "Buone danze! 💃🕺", 3)


def registra(nome, url, copione, out, attesa_sel=None, geo=False, formato="sito"):
    W, H, K = FORMATI[formato]
    tmp = os.path.join(out, "_raw_" + nome)
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(tmp)
    with sync_playwright() as p:
        br = p.chromium.launch()
        extra = dict(geolocation={"latitude": 45.4642, "longitude": 9.19}, permissions=["geolocation"]) if geo else {}
        ctx = br.new_context(viewport={"width": W, "height": H}, device_scale_factor=K, is_mobile=True,
                             has_touch=True, locale="it-IT", **extra)
        ctx.route("**/api/**", _api)
        pg = ctx.new_page()
        pg.goto(url, wait_until="networkidle")
        if attesa_sel:
            pg.wait_for_selector(attesa_sel, timeout=20000)
        pg.wait_for_timeout(800)
        r = Regia(pg)
        r.overlay()
        if formato == "social":
            pg.add_style_tag(content=CSS_SOCIAL)
        cdp = ctx.new_cdp_session(pg)
        frames = []

        def su_frame(ev):
            f = os.path.join(tmp, f"{len(frames):05d}.jpg")
            with open(f, "wb") as fh:
                fh.write(base64.b64decode(ev["data"]))
            frames.append((f, ev["metadata"]["timestamp"]))
            try:
                cdp.send("Page.screencastFrameAck", {"sessionId": ev["sessionId"]})
            except Exception:
                pass

        cdp.on("Page.screencastFrame", su_frame)
        cdp.send("Page.startScreencast", {"format": "jpeg", "quality": 90, "maxWidth": W * K, "maxHeight": H * K})
        r.attendi(0.6)
        copione(r)
        r.attendi(0.4)
        cdp.send("Page.stopScreencast")
        fine = time.time()
        ctx.close()
        br.close()

    lista = os.path.join(tmp, "lista.txt")
    with open(lista, "w") as fh:
        for i, (f, t) in enumerate(frames):
            dur = (frames[i + 1][1] if i + 1 < len(frames) else fine) - t
            fh.write(f"file '{os.path.basename(f)}'\nduration {max(dur, 0.001):.4f}\n")
        fh.write(f"file '{os.path.basename(frames[-1][0])}'\n")
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    mp4 = os.path.join(out, f"howto-{nome}.mp4")
    crf = "26" if formato == "sito" else "20"
    subprocess.run([ff, "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", lista,
                    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2,fps=30", "-c:v", "libx264", "-preset", "slow",
                    "-crf", crf, "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", mp4], check=True)
    subprocess.run([ff, "-y", "-loglevel", "error", "-ss", "1.5", "-i", mp4, "-frames:v", "1", "-q:v", "4",
                    os.path.join(out, f"howto-{nome}.jpg")], check=True)
    shutil.rmtree(tmp, ignore_errors=True)
    durata = fine - frames[0][1]
    print(f"{nome}: {os.path.getsize(mp4) / 1024:.0f} KB, {durata:.1f}s", flush=True)
    return mp4
