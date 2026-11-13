import { giornoRoma } from "../../lib/visite.js";
import { pulisci } from "../../lib/clandestini.js";
import { impronta } from "../../lib/bacheca.js";
import { OPZIONI, preparaSondaggio, pulisciScelte, risultati } from "../../lib/sondaggio.js";

// Sondaggio "Quali rubriche vorresti?" (pagina /rubriche, vedi lib/sondaggio.js).
// GET  /api/sondaggio?token=...  : opzioni, risultati e, se c'è il token, il voto di questo browser
// POST /api/sondaggio            : { token, scelte: [chiavi], proposta } (JSON) -> salva o cambia il voto

const NUOVI_PER_RETE = 15;   // voti nuovi al giorno dalla stessa rete (cambiare il proprio voto non conta)
const json = (o, status = 200) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
});
const tokenOk = (t) => /^[A-Za-z0-9-]{20,100}$/.test(String(t || ""));
const opzioni = OPZIONI.map(([k, t, d]) => ({ k, t, d }));

async function mioVoto(db, token) {
  if (!tokenOk(token)) return null;
  const r = await db.prepare(`SELECT scelte, proposta FROM voti_rubriche WHERE chi = ?1`)
    .bind(await impronta("sondaggio|" + token)).first();
  return r ? { scelte: r.scelte ? r.scelte.split(",") : [], proposta: r.proposta } : null;
}

export async function onRequestGet({ request, env }) {
  if (!env.VISITE) return json({ opzioni, attivo: false });
  const token = new URL(request.url).searchParams.get("token");
  const ris = await risultati(env.VISITE);
  return json({ opzioni, attivo: true, ...ris, mio: await mioVoto(env.VISITE, token) });
}

export async function onRequestPost({ request, env }) {
  const db = env.VISITE;
  if (!db) return json({ errore: "Il sondaggio non è ancora attivo." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  if (d.sito_web) return json({ ok: true });   // campo trappola per i bot
  if (!tokenOk(d.token)) return json({ errore: "Ricarica la pagina e riprova." }, 400);
  const scelte = pulisciScelte(d.scelte);
  const proposta = pulisci(String(d.proposta || "").replace(/\s+/g, " "), 200);
  if (!scelte.length && !proposta) return json({ errore: "Scegli almeno una rubrica o proponine una." }, 400);
  await preparaSondaggio(db);
  const chi = await impronta("sondaggio|" + d.token);
  const ora = new Date().toISOString();
  const giaVotato = await db.prepare(`SELECT 1 FROM voti_rubriche WHERE chi = ?1`).bind(chi).first();
  if (!giaVotato) {
    const rete = await impronta("rete|" + giornoRoma() + "|" + (request.headers.get("cf-connecting-ip") || ""));
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM voti_rubriche WHERE rete = ?1`).bind(rete).first();
    if (n >= NUOVI_PER_RETE) return json({ errore: "Troppi voti da questa rete oggi: riprova domani." }, 429);
    await db.prepare(`INSERT INTO voti_rubriche (chi, scelte, proposta, creato, aggiornato, rete) VALUES (?1, ?2, ?3, ?4, ?4, ?5)`)
      .bind(chi, scelte.join(","), proposta, ora, rete).run();
  } else {
    await db.prepare(`UPDATE voti_rubriche SET scelte = ?2, proposta = ?3, aggiornato = ?4 WHERE chi = ?1`)
      .bind(chi, scelte.join(","), proposta, ora).run();
  }
  return json({ ok: true, ...(await risultati(db)), mio: { scelte, proposta } });
}
