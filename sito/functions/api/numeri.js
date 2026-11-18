// GET /api/numeri?anno=2026 : numeri del sito per la pagina /2026 ("siBalla 2026 in numeri").
// Solo totali, niente di personale: visitatori (primo accesso in assoluto da un browser, vedi conta.js),
// pagine viste, annunci della Bacheca e clandestini approvati nell'anno. I numeri degli eventi arrivano
// invece da numeri-<anno>.json (sito/numeri_anno.py, dallo Sheet).
// Tabella che manca (database appena creato) = 0. In cache un'ora.

const zero = { visitatori: 0, pagine: 0, annunci: 0, clandestini: 0 };

async function numero(db, sql, ...args) {
  try { return (await db.prepare(sql).bind(...args).first())?.n || 0; } catch { return 0; }
}

export async function onRequestGet({ request, env }) {
  const anno = new URL(request.url).searchParams.get("anno") || "";
  const headers = { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "public, max-age=3600" };
  if (!/^20\d\d$/.test(anno)) return new Response(JSON.stringify({ errore: "anno?" }), { status: 400, headers });
  const db = env.VISITE;
  if (!db) return new Response(JSON.stringify({ anno: +anno, ...zero }), { headers });
  const da = `${anno}-01-01`, a = `${anno}-12-31`, daIso = `${anno}-01-01T00:00:00`, aIso = `${+anno + 1}-01-01T00:00:00`;
  const [visitatori, pagine, annunci, clandestini] = await Promise.all([
    numero(db, `SELECT SUM(n) AS n FROM statistiche WHERE tipo = 'visitatori' AND chiave = 'nuovi' AND giorno BETWEEN ?1 AND ?2`, da, a),
    numero(db, `SELECT SUM(n) AS n FROM statistiche WHERE tipo = 'pagina' AND giorno BETWEEN ?1 AND ?2`, da, a),
    numero(db, `SELECT COUNT(*) AS n FROM annunci WHERE stato = 'ok' AND creato >= ?1 AND creato < ?2`, daIso, aIso),
    numero(db, `SELECT COUNT(*) AS n FROM clandestini WHERE stato = 'ok' AND creato >= ?1 AND creato < ?2`, daIso, aIso),
  ]);
  return new Response(JSON.stringify({ anno: +anno, visitatori, pagine, annunci, clandestini }), { headers });
}
