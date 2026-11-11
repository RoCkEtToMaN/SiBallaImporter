import { giornoRoma } from "../../lib/visite.js";
import { preparaClandestini, pulisci, dataSheet, leggiFoto } from "../../lib/clandestini.js";

// GET  /api/clandestini : i clandestini approvati da oggi in poi, nello stesso formato di un foglio di eventi.json
// POST /api/clandestini : nuova proposta dal modulo del sito (JSON); finisce "in attesa" finché non la approvi.

const STILI = ["Lindy Hop", "Balboa", "Blues", "Solo Jazz", "Boogie Woogie", "Charleston", "Shag"];
const MAX_IN_ATTESA = 40;   // freno allo spam: oltre questo numero di proposte da approvare non ne accetta altre
const json = (o, status = 200, extra = {}) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...extra },
});

export async function onRequestGet({ env }) {
  const foglio = {
    nome: "Clandestini",
    colonne: ["Data inizio", "Data fine", "", "", "Nome", "Luogo", "Organizzatore", "Stile", "Gratis", "Riferimento", "Ora"],
    eventi: [],
  };
  if (env.VISITE) {
    try {
      await preparaClandestini(env.VISITE);
      const { results } = await env.VISITE.prepare(
        `SELECT c.*, f.id IS NOT NULL AS foto FROM clandestini c LEFT JOIN clandestini_foto f ON f.id = c.id
         WHERE c.stato = 'ok' AND c.giorno >= ?1 ORDER BY c.giorno, c.ora LIMIT 200`)
        .bind(giornoRoma()).all();
      foglio.eventi = results.map((r) => ({
        id: r.id,
        celle: [dataSheet(r.giorno), "", "", "", r.nome, `Italia, ${r.citta}, ${r.luogo}`, r.dettagli,
          r.stili, r.gratis ? "sì" : "", r.link, r.ora],
        img: r.foto ? `/api/clandestini-foto/${r.id}` : undefined,
        lat: r.lat, lng: r.lng,
        mappa: "https://www.google.com/maps/search/?api=1&query=" + encodeURIComponent(`${r.luogo}, ${r.citta}`),
      }));
    } catch (e) {
      console.log("clandestini non letti: " + e);
    }
  }
  return json(foglio, 200, { "Cache-Control": "public, max-age=60" });
}

// coordinate da Photon (lo stesso geocoding dell'importatore); se non risponde in fretta l'evento resta senza
async function coordinate(luogo, citta) {
  try {
    const q = encodeURIComponent(`${luogo}, ${citta}, Italia`);
    const r = await fetch(`https://photon.komoot.io/api/?q=${q}&limit=1`, {
      headers: { "User-Agent": "siBalla-sito" }, signal: AbortSignal.timeout(4000),
    });
    const f = (await r.json()).features?.[0];
    if (f) return { lng: f.geometry.coordinates[0], lat: f.geometry.coordinates[1] };
  } catch { /* niente coordinate */ }
  return { lat: null, lng: null };
}

async function turnstileOk(env, token, ip) {
  if (!env.TURNSTILE_SECRET) return true;   // captcha non ancora configurato
  const body = new FormData();
  body.append("secret", env.TURNSTILE_SECRET);
  body.append("response", token || "");
  if (ip) body.append("remoteip", ip);
  try {
    const r = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", { method: "POST", body });
    return (await r.json()).success === true;
  } catch { return false; }
}

export async function onRequestPost({ request, env }) {
  const db = env.VISITE;
  if (!db) return json({ errore: "Le proposte non sono ancora attive, riprova più tardi." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  // campo nascosto che compilano solo i bot: fingo che sia andato tutto bene
  if (d.sito_web) return json({ ok: true });
  if (!(await turnstileOk(env, d.turnstile, request.headers.get("cf-connecting-ip"))))
    return json({ errore: "Verifica anti-spam non riuscita, ricarica la pagina e riprova." }, 400);

  const nome = pulisci(d.nome, 100), citta = pulisci(d.citta, 60), luogo = pulisci(d.luogo, 120);
  const giorno = /^\d{4}-\d{2}-\d{2}$/.test(d.giorno || "") ? d.giorno : "";
  const ora = /^\d{2}:\d{2}$/.test(d.ora || "") ? d.ora : "";
  const stili = (Array.isArray(d.stili) ? d.stili : []).filter((s) => STILI.includes(s)).join(", ");
  let link = pulisci(d.link, 300);
  if (link && !/^https?:\/\//i.test(link)) link = "https://" + link;
  if (link && !/^https?:\/\/[^\s/]+\.[^\s]+$/i.test(link)) link = "";
  const dettagli = pulisci(d.dettagli, 300), contatto = pulisci(d.contatto, 120);
  const foto = d.foto ? leggiFoto(d.foto) : null;
  if (d.foto && !foto) return json({ errore: "La foto non va bene: usa un'immagine JPEG, PNG o WebP." }, 400);

  const oggi = giornoRoma(), limite = giornoRoma(new Date(Date.now() + 90 * 86400000));
  if (!nome || !citta || !luogo || !giorno) return json({ errore: "Compila nome, data, città e luogo." }, 400);
  if (giorno < oggi || giorno > limite) return json({ errore: "La data deve essere tra oggi e i prossimi 3 mesi." }, 400);

  try {
    await preparaClandestini(db);
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM clandestini WHERE stato = 'attesa'`).first();
    if (n >= MAX_IN_ATTESA) return json({ errore: "Troppe proposte in attesa, riprova tra qualche ora." }, 429);
    const { lat, lng } = await coordinate(luogo, citta);
    const r = await db.prepare(
      `INSERT INTO clandestini (creato, nome, giorno, ora, citta, luogo, lat, lng, stili, gratis, link, dettagli, contatto)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)`)
      .bind(new Date().toISOString(), nome, giorno, ora, citta, luogo, lat, lng, stili, d.gratis === false ? 0 : 1,
        link, dettagli, contatto).run();
    if (foto) await db.prepare(`INSERT OR REPLACE INTO clandestini_foto (id, tipo, dati) VALUES (?1, ?2, ?3)`)
      .bind(r.meta.last_row_id, foto.tipo, foto.dati).run();
  } catch (e) {
    console.log("clandestino non salvato: " + e);
    return json({ errore: "Non sono riuscito a salvare la proposta, riprova più tardi." }, 500);
  }
  return json({ ok: true });
}
