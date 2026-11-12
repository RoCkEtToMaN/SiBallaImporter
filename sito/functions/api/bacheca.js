import { giornoRoma } from "../../lib/visite.js";
import { pulisci, esc, dataSheet } from "../../lib/clandestini.js";
import { preparaBacheca, CATEGORIE, RUOLI, GIORNI_SCADENZA, impronta, EMAIL_VALIDA } from "../../lib/bacheca.js";

// GET  /api/bacheca : annunci approvati e non scaduti
// POST /api/bacheca : nuovo annuncio dal modulo della pagina /bacheca; resta "in attesa" finché non lo approvi.
//                     { azione: "togli", id, token }: chi l'ha scritto (stesso browser) lo toglie dalla bacheca.

const STILI = ["Lindy Hop", "Balboa", "Blues", "Solo Jazz", "Boogie Woogie", "Charleston", "Shag"];
const MAX_IN_ATTESA = 40;   // freno allo spam
const json = (o, status = 200, extra = {}) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...extra },
});

export async function onRequestGet({ env }) {
  let annunci = [];
  if (env.VISITE) {
    try {
      await preparaBacheca(env.VISITE);
      const { results } = await env.VISITE.prepare(
        `SELECT id, creato, categoria, tipo, evento, giorno, scade, citta, arrivo, ruolo, stile, testo, nome, contatto,
           substr(autore, 1, 16) AS a,   -- per riconoscere i propri annunci nel browser (pulsante "Togli")
           (SELECT COUNT(*) FROM messaggi m WHERE m.annuncio = annunci.id AND m.nascosto = 0) AS messaggi
         FROM annunci WHERE stato = 'ok' AND scade >= ?1 ORDER BY CASE WHEN giorno = '' THEN scade ELSE giorno END, id LIMIT 300`)
        .bind(giornoRoma()).all();
      annunci = results;
    } catch (e) {
      console.log("annunci non letti: " + e);
    }
  }
  return json({ annunci }, 200, { "Cache-Control": "public, max-age=60" });
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
  if (!db) return json({ errore: "La bacheca non è ancora attiva, riprova più tardi." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  if (d.sito_web) return json({ ok: true });   // campo trappola per i bot

  // chi ha scritto l'annuncio lo toglie: vale solo dallo stesso browser (stessa impronta del codice segreto)
  if (d.azione === "togli") {
    const t = String(d.token || "");
    if (!/^[A-Za-z0-9-]{20,100}$/.test(t)) return json({ errore: "Ricarica la pagina e riprova." }, 400);
    await preparaBacheca(db);
    const r = await db.prepare(`UPDATE annunci SET stato = 'no', avvisi = '' WHERE id = ?1 AND autore = ?2 AND autore <> ''`)
      .bind(Number(d.id), await impronta("autore|" + t)).run();
    return r.meta?.changes === 0 ? json({ errore: "Puoi togliere solo i tuoi annunci, dallo stesso browser con cui li hai scritti." }, 403) : json({ ok: true });
  }

  if (!(await turnstileOk(env, d.turnstile, request.headers.get("cf-connecting-ip"))))
    return json({ errore: "Verifica anti-spam non riuscita, ricarica la pagina e riprova." }, 400);

  const categoria = CATEGORIE[d.categoria] ? d.categoria : "";
  const tipo = d.tipo === "offro" || d.tipo === "cerco" ? d.tipo : "";
  if (!tipo) return json({ errore: "Scegli se cerchi o offri." }, 400);
  const citta = pulisci(d.citta, 60), arrivo = categoria === "passaggio" ? pulisci(d.arrivo, 80) : "";
  const evento = pulisci(d.evento, 120), testo = pulisci(d.testo, 400);
  const nome = pulisci(d.nome, 40), contatto = pulisci(d.contatto, 120);
  // email PRIVATA facoltativa per gli avvisi (pubblicazione e nuovi messaggi): mai mostrata
  const avvisi = pulisci(d.avvisi, 120);
  if (avvisi && !EMAIL_VALIDA.test(avvisi)) return json({ errore: "L'email per gli avvisi non sembra valida." }, 400);
  const ruolo = categoria === "partner" && RUOLI.includes(d.ruolo) ? d.ruolo : "";
  const stile = STILI.includes(d.stile) ? d.stile : "";
  const giorno = /^\d{4}-\d{2}-\d{2}$/.test(d.giorno || "") ? d.giorno : "";

  const oggi = giornoRoma();
  const limite = giornoRoma(new Date(Date.now() + 365 * 86400000));
  if (!categoria) return json({ errore: "Scegli il tipo di annuncio." }, 400);
  // il contatto è facoltativo: senza, ci si accorda nella discussione sotto l'annuncio
  if (!citta) return json({ errore: categoria === "passaggio" ? "Scrivi da dove parti." : "Scrivi la tua città." }, 400);
  if (!nome) return json({ errore: "Scrivi il tuo nome o un soprannome." }, 400);
  if (categoria === "passaggio" && !arrivo && !evento) return json({ errore: "Scrivi dove vai (destinazione o evento)." }, 400);
  if (giorno && (giorno < oggi || giorno > limite)) return json({ errore: "La data deve essere tra oggi e i prossimi 12 mesi." }, 400);
  const scade = giorno || giornoRoma(new Date(Date.now() + GIORNI_SCADENZA * 86400000));

  try {
    await preparaBacheca(db);
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM annunci WHERE stato = 'attesa'`).first();
    if (n >= MAX_IN_ATTESA) return json({ errore: "Troppi annunci in attesa, riprova tra qualche ora." }, 429);
    // impronta del codice segreto del browser (lo stesso della discussione): i messaggi che quel browser
    // scrive sotto questo annuncio avranno l'etichetta "autore dell'annuncio"
    const token = String(d.token || "");
    const autore = /^[A-Za-z0-9-]{20,100}$/.test(token) ? await impronta("autore|" + token) : "";
    const r = await db.prepare(
      `INSERT INTO annunci (creato, categoria, tipo, evento, giorno, scade, citta, arrivo, ruolo, stile, testo, nome, contatto, autore, avvisi)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15)`)
      .bind(new Date().toISOString(), categoria, tipo, evento, giorno, scade, citta, arrivo, ruolo, stile, testo, nome, contatto, autore, avvisi).run();
    const id = r.meta.last_row_id;
    waitUntil(avvisa(env, new URL(request.url).origin, { categoria, tipo, evento, giorno, citta, arrivo, ruolo, stile, testo, nome, contatto, avvisi })
      .then((esito) => db.prepare(`UPDATE annunci SET email = ?1 WHERE id = ?2`).bind(esito.slice(0, 500), id).run())
      .catch((e) => console.log("esito email non salvato: " + e)));
  } catch (e) {
    console.log("annuncio non salvato: " + e);
    return json({ errore: "Non sono riuscito a salvare l'annuncio, riprova più tardi." }, 500);
  }
  return json({ ok: true });
}

// --- email di avviso (Mailjet, stesse chiavi e destinatari dei clandestini) ---
const MITTENTE = { Email: "siballanewsletter@gmail.com", Name: "siBalla" };
const DESTINATARI = "Giacomo.lo87@gmail.com";

export async function avvisa(env, origine, a) {
  const mancano = ["MAILJET_API_KEY", "MAILJET_API_SECRET"].filter((k) => !env[k]);
  if (mancano.length) return `non inviata: manca ${mancano.join(" e ")} nelle impostazioni Cloudflare di questo ambiente`;
  const dest = (env.EMAIL_DESTINATARI || DESTINATARI).split(";").map((s) => s.trim()).filter(Boolean);
  const pagina = `${origine}/bacheca-admin` + (env.STATS_KEY ? `?chiave=${encodeURIComponent(env.STATS_KEY)}` : "");
  const titolo = CATEGORIE[a.categoria][a.tipo];
  const riga = (k, v) => v ? `<tr><td style="color:#666;padding:3px 12px 3px 0;vertical-align:top">${k}</td><td style="padding:3px 0">${esc(v)}</td></tr>` : "";
  const html = `<div style="font-family:system-ui,Segoe UI,Roboto,sans-serif;font-size:15px;color:#111">
    <h2 style="margin:0 0 4px">📌 Annuncio da approvare: ${esc(titolo)}</h2>
    <p style="margin:0 0 14px;color:#666">Inviato dalla bacheca del sito, non è ancora visibile.</p>
    <table style="border-collapse:collapse">
      ${riga("Evento", a.evento)}${riga("Data", a.giorno ? dataSheet(a.giorno) : "")}
      ${riga(a.categoria === "passaggio" ? "Da" : "Città", a.citta)}${riga("A", a.arrivo)}
      ${riga("Ruolo", a.ruolo)}${riga("Stile", a.stile)}${riga("Testo", a.testo)}
      ${riga("Firma", a.nome)}${riga("Contatto (pubblico)", a.contatto)}${riga("Email per gli avvisi (privata)", a.avvisi)}
    </table>
    <p style="margin:20px 0"><a href="${esc(pagina)}" style="background:#FFD400;color:#0E0E12;font-weight:700;text-decoration:none;padding:10px 18px;border-radius:10px;display:inline-block">Apri la pagina di approvazione</a></p>
  </div>`;
  const testo = `Annuncio da approvare: ${titolo}\n\n` +
    [a.evento, a.giorno && dataSheet(a.giorno), a.citta + (a.arrivo ? " → " + a.arrivo : ""), a.ruolo, a.stile, a.testo,
      `${a.nome} · ${a.contatto}`].filter(Boolean).join("\n") + `\n\nApprova o rifiuta: ${pagina}\n`;
  const prova = /^[^.]+\.[^.]+\.pages\.dev$|^(localhost|127\.0\.0\.1)$/.test(new URL(origine).hostname);
  try {
    const r = await fetch("https://api.mailjet.com/v3.1/send", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: "Basic " + btoa(`${env.MAILJET_API_KEY.trim()}:${env.MAILJET_API_SECRET.trim()}`) },
      body: JSON.stringify({ Messages: [{
        From: MITTENTE, To: dest.map((Email) => ({ Email })),
        Subject: (prova ? "[PROVA] " : "") + `📌 Bacheca: ${titolo} (${a.citta}${a.evento ? ", " + a.evento : ""})`,
        TextPart: testo, HTMLPart: html,
      }] }),
    });
    const risposta = await r.text();
    let j = {}, m = null;
    try { j = JSON.parse(risposta); m = j.Messages?.[0]; } catch { /* non JSON */ }
    if (r.ok && m?.Status === "success") return `inviata a ${dest.join(", ")}`;
    const motivo = m?.Errors?.map((x) => x.ErrorMessage).join("; ") || j.ErrorMessage || risposta.slice(0, 300);
    return `non inviata (Mailjet HTTP ${r.status}): ${motivo}`;
  } catch (e) {
    return "non inviata: " + e;
  }
}
