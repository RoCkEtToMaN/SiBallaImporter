import { preparaTabella, giornoRoma } from "../../lib/visite.js";

// POST /api/visita: chiamata da index.html a ogni apertura della pagina, conta +1 per giorno/luogo.
export async function onRequestPost({ request, env }) {
  const db = env.VISITE;
  if (!db) return new Response(null, { status: 204 });   // database non ancora collegato: non conta
  const cf = request.cf || {};
  const ua = request.headers.get("user-agent") || "";
  if (/bot|crawl|spider|preview|facebookexternalhit|whatsapp|telegram/i.test(ua)) return new Response(null, { status: 204 });
  try {
    await preparaTabella(db);
    await db.prepare(
      `INSERT INTO visite (giorno, paese, regione, citta, n) VALUES (?1, ?2, ?3, ?4, 1)
       ON CONFLICT (giorno, paese, regione, citta) DO UPDATE SET n = n + 1`
    ).bind(giornoRoma(), cf.country || "?", cf.region || "?", cf.city || "?").run();
  } catch (e) {
    console.log("visita non contata: " + e);
  }
  return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
}
