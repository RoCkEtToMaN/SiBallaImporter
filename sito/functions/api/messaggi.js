import { giornoRoma } from "../../lib/visite.js";
import { pulisci, esc } from "../../lib/clandestini.js";
import { preparaBacheca, impronta, inviaEmail, SOGLIA_SEGNALAZIONI } from "../../lib/bacheca.js";

// Discussione sotto gli annunci della bacheca. I messaggi NON passano dall'approvazione: compaiono subito.
// GET  /api/messaggi?annuncio=ID   : messaggi visibili di un annuncio approvato e non scaduto
// POST /api/messaggi               : { azione: "scrivi" | "cancella" | "segnala", ... } (JSON)
// Chi scrive è riconosciuto da un codice segreto che il suo browser genera e ricorda (token): sul server
// resta solo la sua impronta, che serve per cancellare i propri messaggi e per contare le segnalazioni.

const LIMITE_MINUTO = 5, LIMITE_GIORNO = 60;   // messaggi per persona (o per rete)
const MAX_PER_ANNUNCIO = 300;
const json = (o, status = 200) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
});

async function annuncioAperto(db, id) {
  return db.prepare(`SELECT id, citta, arrivo, evento FROM annunci WHERE id = ?1 AND stato = 'ok' AND scade >= ?2`)
    .bind(id, giornoRoma()).first();
}

export async function onRequestGet({ request, env }) {
  const id = Number(new URL(request.url).searchParams.get("annuncio"));
  if (!env.VISITE || !id) return json({ messaggi: [] });
  await preparaBacheca(env.VISITE);
  if (!(await annuncioAperto(env.VISITE, id))) return json({ messaggi: [], chiuso: true });
  // "aut" = 1 se il messaggio viene dal browser che ha scritto l'annuncio
  const { results } = await env.VISITE.prepare(
    `SELECT m.id, m.creato, m.nick, m.testo, substr(m.autore, 1, 16) AS a,
       (an.autore <> '' AND an.autore = m.autore) AS aut
     FROM messaggi m JOIN annunci an ON an.id = m.annuncio
     WHERE m.annuncio = ?1 AND m.nascosto = 0 ORDER BY m.id LIMIT ?2`).bind(id, MAX_PER_ANNUNCIO).all();
  return json({ messaggi: results });
}

export async function onRequestPost({ request, env, waitUntil }) {
  const db = env.VISITE;
  if (!db) return json({ errore: "La discussione non è ancora attiva." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  if (d.sito_web) return json({ ok: true });   // campo trappola per i bot
  const token = String(d.token || "");
  if (!/^[A-Za-z0-9-]{20,100}$/.test(token)) return json({ errore: "Ricarica la pagina e riprova." }, 400);
  const autore = await impronta("autore|" + token);
  await preparaBacheca(db);

  if (d.azione === "cancella") {
    await db.prepare(`DELETE FROM messaggi WHERE id = ?1 AND autore = ?2`).bind(Number(d.id), autore).run();
    return json({ ok: true });
  }

  if (d.azione === "segnala") {
    const id = Number(d.id);
    const m = await db.prepare(`SELECT m.*, a.citta, a.arrivo, a.evento FROM messaggi m JOIN annunci a ON a.id = m.annuncio WHERE m.id = ?1`).bind(id).first();
    if (!m || m.autore === autore) return json({ ok: true });
    await db.prepare(`INSERT OR IGNORE INTO segnalazioni (messaggio, chi, creato) VALUES (?1, ?2, ?3)`)
      .bind(id, autore, new Date().toISOString()).run();
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM segnalazioni WHERE messaggio = ?1`).bind(id).first();
    if (n >= SOGLIA_SEGNALAZIONI && !m.nascosto) {
      await db.prepare(`UPDATE messaggi SET nascosto = 1 WHERE id = ?1`).bind(id).run();
      const pagina = new URL("/bacheca-admin" + (env.STATS_KEY ? "?chiave=" + encodeURIComponent(env.STATS_KEY) : ""), request.url).href;
      const prova = /^[^.]+\.[^.]+\.pages\.dev$|^(localhost|127\.0\.0\.1)$/.test(new URL(request.url).hostname);
      waitUntil(inviaEmail(env, (prova ? "[PROVA] " : "") + `⚠️ Bacheca: messaggio nascosto dopo ${n} segnalazioni`,
        `<div style="font-family:system-ui,sans-serif;font-size:15px"><h2 style="margin:0 0 8px">⚠️ Messaggio nascosto</h2>
         <p>Annuncio: ${esc(m.citta)}${m.arrivo ? " → " + esc(m.arrivo) : ""}${m.evento ? " · " + esc(m.evento) : ""}</p>
         <p><b>${esc(m.nick)}</b>: ${esc(m.testo)}</p>
         <p><a href="${esc(pagina)}">Apri la pagina della bacheca</a> per cancellarlo o renderlo di nuovo visibile.</p></div>`,
        `Messaggio nascosto dopo ${n} segnalazioni\n\n${m.nick}: ${m.testo}\n\n${pagina}\n`).catch(() => {}));
    }
    return json({ ok: true, nascosto: n >= SOGLIA_SEGNALAZIONI });
  }

  // scrivi
  const id = Number(d.annuncio);
  const nick = pulisci(d.nick, 30), testo = pulisci(String(d.testo || "").replace(/\r/g, ""), 500);
  if (nick.length < 2) return json({ errore: "Scegli un nickname di almeno 2 lettere." }, 400);
  if (!testo) return json({ errore: "Scrivi un messaggio." }, 400);
  if (!(await annuncioAperto(db, id))) return json({ errore: "Questo annuncio non è più in bacheca." }, 404);

  const rete = await impronta("rete|" + giornoRoma() + "|" + (request.headers.get("cf-connecting-ip") || ""));
  const unMinutoFa = new Date(Date.now() - 60000).toISOString(), oggi = giornoRoma() + "T00:00";
  const { minuto, giorno } = await db.prepare(
    `SELECT SUM(creato >= ?1) AS minuto, COUNT(*) AS giorno FROM messaggi
     WHERE (autore = ?2 OR rete = ?3) AND creato >= ?4`).bind(unMinutoFa, autore, rete, oggi).first();
  if ((minuto || 0) >= LIMITE_MINUTO) return json({ errore: "Stai scrivendo troppo in fretta: aspetta un minuto." }, 429);
  if ((giorno || 0) >= LIMITE_GIORNO) return json({ errore: "Hai raggiunto il limite di messaggi per oggi." }, 429);
  const { tot } = await db.prepare(`SELECT COUNT(*) AS tot FROM messaggi WHERE annuncio = ?1`).bind(id).first();
  if (tot >= MAX_PER_ANNUNCIO) return json({ errore: "Questa discussione è piena." }, 429);

  await db.prepare(`INSERT INTO messaggi (annuncio, creato, nick, testo, autore, rete) VALUES (?1, ?2, ?3, ?4, ?5, ?6)`)
    .bind(id, new Date().toISOString(), nick, testo, autore, rete).run();
  // pulizia: discussioni di annunci scaduti da più di 7 giorni
  waitUntil(db.prepare(`DELETE FROM messaggi WHERE annuncio IN (SELECT id FROM annunci WHERE scade < ?1)`)
    .bind(giornoRoma(new Date(Date.now() - 7 * 86400000))).run().catch(() => {}));
  return json({ ok: true });
}
