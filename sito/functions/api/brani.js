import { pulisci, esc } from "../../lib/clandestini.js";
import { inviaEmail, prefissoProva } from "../../lib/bacheca.js";
import { antibotOk } from "../../lib/antibot.js";

// POST /api/brani : "Proponi un brano" della pagina Musica. Salva la proposta nel D1 (binding VISITE, tabella
// brani_proposti) e manda un'email a chi gestisce il sito. Non pubblica niente: i brani si aggiungono a mano
// nella scheda "Musica" dello Sheet.

const MAX_AL_GIORNO = 200;   // freno allo spam
const VELOCITA = ["Lenta", "Media", "Veloce", "Band dal vivo"];
const json = (o, status = 200) => new Response(JSON.stringify(o), {
  status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
});

export async function onRequestPost({ request, env, waitUntil }) {
  const db = env.VISITE;
  if (!db) return json({ errore: "Le proposte non sono ancora attive, riprova più tardi." }, 503);
  let d;
  try { d = await request.json(); } catch { return json({ errore: "Richiesta non valida." }, 400); }
  if (d.sito_web) return json({ ok: true });   // campo trappola per i bot
  if (!(await antibotOk(env, d.turnstile, request.headers.get("cf-connecting-ip"))))
    return json({ errore: "Verifica anti-spam non riuscita: aspetta un attimo e riprova." }, 400);

  const titolo = pulisci(d.titolo, 120), artista = pulisci(d.artista, 120);
  let link = pulisci(d.link, 300);
  if (link && !/^https?:\/\//i.test(link)) link = "https://" + link;
  if (link && !/^https?:\/\/[^\s/]+\.[^\s]+$/i.test(link)) link = "";
  const velocita = VELOCITA.includes(d.velocita) ? d.velocita : "";
  if (!titolo) return json({ errore: "Scrivi almeno il titolo del brano." }, 400);

  try {
    await db.prepare(
      `CREATE TABLE IF NOT EXISTS brani_proposti (
         id INTEGER PRIMARY KEY AUTOINCREMENT, creato TEXT NOT NULL,
         titolo TEXT NOT NULL, artista TEXT NOT NULL DEFAULT '', link TEXT NOT NULL DEFAULT '', velocita TEXT NOT NULL DEFAULT ''
       )`).run();
    const ieri = new Date(Date.now() - 86400000).toISOString();
    const { n } = await db.prepare(`SELECT COUNT(*) AS n FROM brani_proposti WHERE creato >= ?1`).bind(ieri).first();
    if (n >= MAX_AL_GIORNO) return json({ errore: "Troppe proposte oggi, riprova domani." }, 429);
    await db.prepare(`INSERT INTO brani_proposti (creato, titolo, artista, link, velocita) VALUES (?1, ?2, ?3, ?4, ?5)`)
      .bind(new Date().toISOString(), titolo, artista, link, velocita).run();
  } catch (e) {
    console.log("brano non salvato: " + e);
    return json({ errore: "Non sono riuscito a salvare la proposta, riprova più tardi." }, 500);
  }

  const riga = (k, v) => v ? `<tr><td style="color:#666;padding:3px 12px 3px 0">${k}</td><td>${esc(v)}</td></tr>` : "";
  waitUntil(inviaEmail(env, prefissoProva(request.url) + `🎵 Brano proposto: ${titolo}${artista ? " – " + artista : ""}`,
    `<div style="font-family:system-ui,sans-serif;font-size:15px"><h2 style="margin:0 0 8px">🎵 Brano proposto dalla pagina Musica</h2>
     <table>${riga("Titolo", titolo)}${riga("Artista", artista)}${riga("Velocità", velocita)}${riga("Link", link)}</table>
     <p style="color:#666">Se va bene, aggiungilo come riga "brano" nella scheda Musica dello Sheet.</p></div>`,
    `Brano proposto: ${titolo}${artista ? " - " + artista : ""}\nVelocità: ${velocita || "non indicata"}\n${link}\n`)
    .catch((e) => console.log("email brano non inviata: " + e)));
  return json({ ok: true });
}
