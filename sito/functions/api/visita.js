import { preparaTabella, giornoRoma } from "../../lib/visite.js";

// POST /api/visita: chiamata da conta.js a ogni apertura di una pagina (t = "vista") e a ogni click
// su un link esterno (t = "click"). Corpo JSON in text/plain (sendBeacon); corpo vuoto = vista della home
// (le vecchie versioni di index.html rimaste in cache mandavano il beacon senza dati).

const BOT = /bot|crawl|spider|preview|facebookexternalhit|whatsapp|telegram|headless|lighthouse/i;

function pulisci(s, max) {
  return String(s || "").replace(/[\x00-\x1f<>]/g, "").replace(/\s+/g, " ").trim().slice(0, max);
}

// provenienza leggibile: Google, Facebook, Instagram, WhatsApp... altrimenti il dominio senza "www."
function fonte(f) {
  f = pulisci(f, 60).toLowerCase();
  if (!f) return "(diretto / app / link salvato)";
  const noti = [
    [/(^|\.)google\./, "Google"], [/(^|\.)bing\.com$/, "Bing"], [/duckduckgo/, "DuckDuckGo"],
    [/(^|\.)(facebook\.com|fb\.com|fb\.me)$|^facebook$|^fb$/, "Facebook"],
    [/(^|\.)instagram\.com$|^instagram$|^ig$/, "Instagram"],
    [/whatsapp|^wa\.me$|^wa$/, "WhatsApp"], [/^t\.co$|(^|\.)(x|twitter)\.com$/, "X / Twitter"],
    [/telegram|^t\.me$/, "Telegram"], [/(^|\.)youtube\.com$|^youtu\.be$/, "YouTube"],
    [/(^|\.)tiktok\.com$|^tiktok$/, "TikTok"], [/(^|\.)linkedin\.com$|^lnkd\.in$/, "LinkedIn"],
  ];
  for (const [re, nome] of noti) if (re.test(f)) return nome;
  return f.replace(/^www\./, "");
}

function dispositivo(ua) {
  if (/ipad|tablet|android(?!.*mobile)/i.test(ua)) return "Tablet";
  if (/mobi|iphone|ipod|android/i.test(ua)) return "Telefono";
  return "Computer";
}

export async function onRequestPost({ request, env }) {
  const db = env.VISITE;
  if (!db) return new Response(null, { status: 204 });   // database non ancora collegato: non conta
  const ua = request.headers.get("user-agent") || "";
  if (BOT.test(ua)) return new Response(null, { status: 204 });
  try {
    let d = {};
    try { d = JSON.parse((await request.text()) || "{}") || {}; } catch { d = {}; }
    await preparaTabella(db);
    const giorno = giornoRoma();
    const stat = (tipo, chiave) => db.prepare(
      `INSERT INTO statistiche (giorno, tipo, chiave, n) VALUES (?1, ?2, ?3, 1)
       ON CONFLICT (giorno, tipo, chiave) DO UPDATE SET n = n + 1`).bind(giorno, tipo, chiave);
    const op = [];
    if (d.t === "click") {
      const cosa = pulisci(d.cosa, 90), tipo = pulisci(d.tipo, 30);
      if (cosa) op.push(stat("click", tipo ? `${cosa} · ${tipo}` : cosa));
    } else {
      const cf = request.cf || {};
      op.push(db.prepare(
        `INSERT INTO visite (giorno, paese, regione, citta, n) VALUES (?1, ?2, ?3, ?4, 1)
         ON CONFLICT (giorno, paese, regione, citta) DO UPDATE SET n = n + 1`
      ).bind(giorno, cf.country || "?", cf.region || "?", cf.city || "?"));
      const pagina = /^\/[a-z0-9\-\/]{0,40}$/i.test(d.pagina || "") ? d.pagina.toLowerCase() : "/";
      op.push(stat("pagina", pagina), stat("fonte", fonte(d.fonte)), stat("disp", dispositivo(ua)));
      if (d.nuovoOggi) op.push(stat("visitatori", "giorno"));
      if (d.nuovoMese) op.push(stat("visitatori", "mese"));
      if (d.nuovo) op.push(stat("visitatori", "nuovi"));
    }
    if (op.length) await db.batch(op);
  } catch (e) {
    console.log("visita non contata: " + e);
  }
  return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
}
