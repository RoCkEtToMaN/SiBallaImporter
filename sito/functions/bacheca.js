import { giornoRoma } from "../lib/visite.js";
import { preparaBacheca, CATEGORIE } from "../lib/bacheca.js";

// /bacheca?annuncio=ID : la pagina è la stessa (bacheca.html), ma titolo e descrizione per l'anteprima
// (WhatsApp, Telegram, Instagram…) diventano quelli dell'annuncio condiviso con "↗ Condividi".
// Senza ?annuncio, o se l'annuncio non è visibile, la pagina esce com'è.

export async function onRequestGet(context) {
  const { request, env, next } = context;
  const pagina = await next();
  if (!(pagina.headers.get("Content-Type") || "").includes("text/html")) return pagina;
  const idm = new URL(request.url).searchParams.get("mercatino") || "";
  if (/^[0-9a-f]{6,20}$/.test(idm)) return anteprimaMercatino(context, pagina, idm);
  const id = Number(new URL(request.url).searchParams.get("annuncio")) || 0;
  if (!id || !env.VISITE) return pagina;
  let a = null;
  try {
    await preparaBacheca(env.VISITE);
    a = await env.VISITE.prepare(
      `SELECT categoria, tipo, evento, giorno, citta, arrivo, ruolo, stile, testo, nome
       FROM annunci WHERE id = ?1 AND stato = 'ok' AND scade >= ?2`).bind(id, giornoRoma()).first();
  } catch (e) {
    console.log("anteprima annuncio non letta: " + e);
  }
  if (!a) return pagina;

  const c = CATEGORIE[a.categoria] || CATEGORIE.passaggio;
  const quando = a.giorno
    ? new Date(a.giorno + "T12:00:00Z").toLocaleDateString("it-IT", { weekday: "short", day: "numeric", month: "long", timeZone: "UTC" })
    : "";
  const titolo = [c[a.tipo] || c.cerco, a.citta + (a.categoria === "passaggio" && a.arrivo ? " → " + a.arrivo : ""), a.evento]
    .filter(Boolean).join(" · ");
  const extra = [quando, a.ruolo && a.ruolo.charAt(0).toUpperCase() + a.ruolo.slice(1), a.stile].filter(Boolean).join(" · ");
  const testo = String(a.testo || "").replace(/\s+/g, " ").trim();
  const descrizione = [extra, testo.length > 160 ? testo.slice(0, 157) + "…" : testo, "Scrivi a " + a.nome + " sulla bacheca di siBalla"]
    .filter(Boolean).join(" — ");
  const url = new URL(request.url);
  const link = `${url.origin}/bacheca?annuncio=${id}`;

  return conMeta(pagina, titolo + " · Bacheca siBalla", titolo, descrizione, link, `${url.origin}/og.png`);
}

// /bacheca?mercatino=ID : mercatino trovato da siBalla (mercatini.json, id fatto da esporta_sheet.py)
async function anteprimaMercatino({ request, env }, pagina, idm) {
  let m = null;
  try {
    const r = await env.ASSETS.fetch(new URL("/mercatini.json", request.url));
    if (r.ok) m = ((await r.json()).mercatini || []).find((x) => x.id === idm) || null;
  } catch (e) {
    console.log("anteprima mercatino non letta: " + e);
  }
  if (!m || !m.titolo) return pagina;
  const url = new URL(request.url);
  const oggi = giornoRoma();
  const giorni = (m.giorni || []).filter((g) => g >= oggi);
  const fmt = (g) => new Date(g + "T12:00:00Z").toLocaleDateString("it-IT", { weekday: "short", day: "numeric", month: "long", timeZone: "UTC" });
  const quando = giorni.length ? fmt(giorni[0]) + (giorni.length > 1 ? ` (e altri ${giorni.length - 1} giorni)` : "") : "";
  const titolo = ["🛍️ Mercatino vintage", m.citta, m.titolo].filter(Boolean).join(" · ");
  const descrizione = [quando, m.organizzatore ? "Organizza: " + m.organizzatore : "", "Segnalato dalla bacheca di siBalla"].filter(Boolean).join(" — ");
  const img = /^https:\/\//i.test(m.img || "") ? m.img : m.img && m.img.startsWith("/") ? url.origin + m.img : `${url.origin}/og.png`;
  return conMeta(pagina, titolo + " · Bacheca siBalla", titolo, descrizione, `${url.origin}/bacheca?mercatino=${idm}`, img, img.endsWith("/og.png"));
}

// titolo, descrizione e meta og: per l'anteprima nelle chat. I testi arrivano dagli utenti o dallo Sheet: protetti a mano.
function conMeta(pagina, titoloPagina, titolo, descrizione, link, immagine, immagineOg = true) {
  const e = (s) => String(s).replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  return new HTMLRewriter()
    .on("title", { element(el) { el.setInnerContent(titoloPagina); } })
    .on('meta[name="description"]', { element(el) { el.setAttribute("content", descrizione); } })
    .on("head", {
      element(el) {
        el.append(
          `<meta property="og:type" content="website">` +
          `<meta property="og:site_name" content="siBalla">` +
          `<meta property="og:title" content="${e(titolo)}">` +
          `<meta property="og:description" content="${e(descrizione)}">` +
          `<meta property="og:url" content="${e(link)}">` +
          `<meta property="og:image" content="${e(immagine)}">` +
          (immagineOg ? `<meta property="og:image:width" content="1200"><meta property="og:image:height" content="630">` : "") +
          `<meta property="og:locale" content="it_IT"><meta name="twitter:card" content="summary_large_image">`,
          { html: true });
      },
    })
    .transform(pagina);
}
