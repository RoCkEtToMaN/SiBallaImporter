import { pulisci, esc } from "../../lib/clandestini.js";
import { inviaEmail, prefissoProva } from "../../lib/bacheca.js";
import { antibotOk } from "../../lib/antibot.js";

// POST /api/community: cinque campi obbligatori; email privata, nessuna pubblicazione automatica.
// 200 {ok:true,id}; 400 dati/antispam non validi; 429 limite giornaliero; 503 DB assente; 500 salvataggio fallito.
const REGIONI = ["Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna", "Friuli-Venezia Giulia", "Lazio", "Liguria", "Lombardia", "Marche", "Molise", "Piemonte", "Puglia", "Sardegna", "Sicilia", "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto"];
const json = (o, status = 200) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
});

export async function onRequestPost({ request, env, waitUntil }) {
  const db = env.VISITE;
  if (!db) return json({ errore: "Le richieste non sono ancora attive, riprova più tardi." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  if (!d || typeof d !== "object" || Array.isArray(d)) return json({ errore: "Richiesta non valida." }, 400);
  if (d.sito_web) return json({ ok: true });
  if (!(await antibotOk(env, d.turnstile, request.headers.get("cf-connecting-ip"))))
    return json({ errore: "Verifica anti-spam non riuscita: riprova." }, 400);
  const limiti = { titolo:120, descrizione:1500, link:500, regione:80, email:254 };
  for (const [campo, limite] of Object.entries(limiti)) {
    if (typeof d[campo] !== "string" || !d[campo].trim() || d[campo].length > limite)
      return json({ errore: "Compila tutti i campi rispettando le lunghezze indicate." }, 400);
  }
  const titolo = pulisci(d.titolo, 120), descrizione = pulisci(d.descrizione, 1500);
  const regione = d.regione.trim(), email = d.email.trim(), link = d.link.trim();
  if (!titolo || !descrizione || !REGIONI.includes(regione)) return json({ errore: "Controlla titolo, descrizione e regione." }, 400);
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return json({ errore: "Inserisci un indirizzo email valido." }, 400);
  try {
    const url = new URL(link);
    if (!["https:", "http:"].includes(url.protocol) || !url.hostname.includes(".") || url.username || url.password)
      throw new Error("link");
  } catch { return json({ errore: "Inserisci un link valido che inizi con https:// o http://." }, 400); }
  let id;
  try {
    await db.prepare(`CREATE TABLE IF NOT EXISTS community_proposte (
      id INTEGER PRIMARY KEY AUTOINCREMENT, creato TEXT NOT NULL, titolo TEXT NOT NULL,
      descrizione TEXT NOT NULL, link TEXT NOT NULL, regione TEXT NOT NULL, email TEXT NOT NULL,
      stato TEXT NOT NULL DEFAULT 'da valutare', notifica TEXT NOT NULL DEFAULT 'in attesa'
    )`).run();
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM community_proposte WHERE creato >= ?1`)
      .bind(new Date(Date.now() - 86400000).toISOString()).first();
    if (n >= 200) return json({ errore: "Troppe richieste oggi, riprova domani." }, 429);
    const result = await db.prepare(`INSERT INTO community_proposte (creato, titolo, descrizione, link, regione, email)
      VALUES (?1, ?2, ?3, ?4, ?5, ?6)`)
      .bind(new Date().toISOString(), titolo, descrizione, link, regione, email).run();
    id = result.meta.last_row_id;
  } catch (e) {
    console.log("community non salvata: " + e);
    return json({ errore: "Non sono riuscito a salvare la richiesta. Riprova più tardi." }, 500);
  }
  const righe = [["Titolo", titolo], ["Descrizione", descrizione], ["Link", link], ["Regione", regione], ["Email di contatto (privata)", email]];
  waitUntil((async () => {
    const notifica = await inviaEmail(env, prefissoProva(request.url) + `Community proposta: ${titolo}`,
      `<div style="font-family:system-ui,sans-serif"><h2>Nuova richiesta community #${id}</h2><table>${righe.map(([k,v]) => `<tr><th style="text-align:left;vertical-align:top;padding-right:16px">${k}</th><td style="white-space:pre-wrap">${esc(v)}</td></tr>`).join("")}</table><p>Valuta la richiesta prima di aggiungerla alla scheda Community dello Sheet. Non pubblicare l'email di contatto.</p></div>`,
      `Nuova richiesta community #${id}\n\n` + righe.map(([k,v]) => `${k}: ${v}`).join("\n\n"));
    await db.prepare(`UPDATE community_proposte SET notifica = ?1 WHERE id = ?2`).bind(notifica, id).run();
    if (!notifica.startsWith("inviata")) console.log("notifica community #" + id + ": " + notifica);
  })().catch((e) => console.log("notifica community: " + e)));
  return json({ ok: true, id });
}