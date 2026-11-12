import { giornoRoma } from "../../lib/visite.js";
import { preparaClandestini, pulisci, dataSheet, leggiFoto, esc } from "../../lib/clandestini.js";

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
  if (!env.TURNSTILE_SECRET || !env.TURNSTILE_SITEKEY) return true;   // anti-bot acceso solo con entrambe le chiavi (vedi lib/antibot.js)
  const body = new FormData();
  body.append("secret", env.TURNSTILE_SECRET);
  body.append("response", token || "");
  if (ip) body.append("remoteip", ip);
  try {
    const r = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", { method: "POST", body });
    return (await r.json()).success === true;
  } catch { return false; }
}

export async function onRequestPost({ request, env, waitUntil }) {
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
    // email di avviso: parte dopo la risposta, così chi propone non aspetta Mailjet
    // l'esito ("inviata" o il motivo dell'errore) si vede nella pagina di approvazione
    const id = r.meta.last_row_id;
    waitUntil(avvisa(env, new URL(request.url).origin, {
      nome, giorno, ora, citta, luogo, stili, link, dettagli, contatto, foto, coordinate: lat != null,
    }).then((esito) => db.prepare(`UPDATE clandestini SET email = ?1 WHERE id = ?2`).bind(esito.slice(0, 500), id).run())
      .catch((e) => console.log("esito email non salvato: " + e)));
  } catch (e) {
    console.log("clandestino non salvato: " + e);
    return json({ errore: "Non sono riuscito a salvare la proposta, riprova più tardi." }, 500);
  }
  return json({ ok: true });
}

// --- email di avviso con Mailjet (stesso mittente e destinatario dell'importatore) ---
// Secrets nel progetto Pages: MAILJET_API_KEY, MAILJET_API_SECRET; variabile facoltativa EMAIL_DESTINATARI
// (più indirizzi separati da ";"). Senza le chiavi l'email semplicemente non parte.
const MITTENTE = { Email: "siballanewsletter@gmail.com", Name: "siBalla" };
const DESTINATARI = "Giacomo.lo87@gmail.com";   // come Email:Recipients di appsettings.json

export async function avvisa(env, origine, c) {
  const mancano = ["MAILJET_API_KEY", "MAILJET_API_SECRET"].filter((k) => !env[k]);
  if (mancano.length) return `non inviata: manca ${mancano.join(" e ")} nelle impostazioni Cloudflare di questo ambiente`;
  const a = (env.EMAIL_DESTINATARI || DESTINATARI).split(";").map((s) => s.trim()).filter(Boolean);
  const pagina = `${origine}/clandestini-admin` + (env.STATS_KEY ? `?chiave=${encodeURIComponent(env.STATS_KEY)}` : "");
  const quando = new Date(c.giorno + "T12:00:00Z").toLocaleDateString("it-IT",
    { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }) + (c.ora ? ` alle ${c.ora}` : "");
  const riga = (k, v) => v ? `<tr><td style="color:#666;padding:3px 12px 3px 0;vertical-align:top">${k}</td><td style="padding:3px 0">${esc(v)}</td></tr>` : "";
  const html = `<div style="font-family:system-ui,Segoe UI,Roboto,sans-serif;font-size:15px;color:#111">
    <h2 style="margin:0 0 4px">🎷 Nuovo clandestino da approvare</h2>
    <p style="margin:0 0 14px;color:#666">Proposto dal sito, non è ancora visibile.</p>
    ${c.foto ? `<img src="cid:foto" alt="" style="max-width:100%;width:480px;border-radius:10px;margin-bottom:12px">` : ""}
    <table style="border-collapse:collapse">
      ${riga("Nome", c.nome)}${riga("Quando", quando)}${riga("Dove", `${c.citta} · ${c.luogo}`)}
      ${riga("Coordinate", c.coordinate ? "trovate" : "non trovate (non comparirà in \"Vicino a me\")")}
      ${riga("Stili", c.stili)}${riga("Dettagli", c.dettagli)}${riga("Link", c.link)}${riga("Contatto", c.contatto)}
    </table>
    <p style="margin:20px 0"><a href="${esc(pagina)}" style="background:#FFD400;color:#0E0E12;font-weight:700;text-decoration:none;padding:10px 18px;border-radius:10px;display:inline-block">Apri la pagina di approvazione</a></p>
  </div>`;
  const testo = `Nuovo clandestino da approvare\n\n${c.nome}\n${quando}\n${c.citta} · ${c.luogo}\n` +
    (c.dettagli ? c.dettagli + "\n" : "") + (c.link ? c.link + "\n" : "") + (c.contatto ? "Contatto: " + c.contatto + "\n" : "") +
    `\nApprova o rifiuta: ${pagina}\n`;
  const messaggio = {
    From: MITTENTE, To: a.map((Email) => ({ Email })),
    // dalle anteprime (<ramo>.siballa.pages.dev) o in locale: "[PROVA]" nell'oggetto, per distinguerle da quelle vere
    Subject: (/^[^.]+\.[^.]+\.pages\.dev$|^(localhost|127\.0\.0\.1)$/.test(new URL(origine).hostname) ? "[PROVA] " : "") +
      `🎷 Clandestino da approvare: ${c.nome} (${c.citta}, ${dataSheet(c.giorno).slice(0, 5)})`,
    TextPart: testo, HTMLPart: html,
  };
  if (c.foto) {
    let bin = "";
    for (let i = 0; i < c.foto.dati.length; i += 0x8000) bin += String.fromCharCode.apply(null, c.foto.dati.subarray(i, i + 0x8000));
    messaggio.InlinedAttachments = [{ ContentType: c.foto.tipo, Filename: "foto", ContentID: "foto", Base64Content: btoa(bin) }];
  }
  try {
    const r = await fetch("https://api.mailjet.com/v3.1/send", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: "Basic " + btoa(`${env.MAILJET_API_KEY.trim()}:${env.MAILJET_API_SECRET.trim()}`) },
      body: JSON.stringify({ Messages: [messaggio] }),
    });
    const risposta = await r.text();
    // Mailjet risponde 200 anche quando rifiuta il singolo messaggio: conta Messages[0].Status
    let j = {}, m = null;
    try { j = JSON.parse(risposta); m = j.Messages?.[0]; } catch { /* risposta non JSON */ }
    if (r.ok && m?.Status === "success") return `inviata a ${a.join(", ")}`;
    const motivo = m?.Errors?.map((x) => x.ErrorMessage).join("; ") || j.ErrorMessage || risposta.slice(0, 300);
    console.log(`email clandestino non inviata: HTTP ${r.status} ${motivo}`);
    return `non inviata (Mailjet HTTP ${r.status}): ${motivo}`;
  } catch (e) {
    console.log("email clandestino non inviata: " + e);
    return "non inviata: " + e;
  }
}
