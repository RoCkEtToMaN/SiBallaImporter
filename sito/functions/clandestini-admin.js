import { giornoRoma } from "../lib/visite.js";
import { preparaClandestini, esc, uguali } from "../lib/clandestini.js";
import { avvisa } from "./api/clandestini.js";

// GET  /clandestini-admin?chiave=... : pagina nascosta con i clandestini proposti dagli utenti, da approvare o rifiutare.
// POST /clandestini-admin            : pulsanti Approva / Rifiuta / Togli (modulo con chiave, id e azione).
// Stessa chiave delle statistiche (secret STATS_KEY); senza la chiave giusta risponde 404.

const nonTrovata = () => new Response("Not found", { status: 404 });
const AZIONI = { approva: "ok", rifiuta: "no", togli: "no", riapri: "attesa" };

export async function onRequestPost({ request, env }) {
  const f = await request.formData();
  const chiave = String(f.get("chiave") || "");
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY) || !env.VISITE) return nonTrovata();
  const stato = AZIONI[f.get("azione")], id = Number(f.get("id"));
  await preparaClandestini(env.VISITE);
  if (stato && id) {
    await env.VISITE.prepare(`UPDATE clandestini SET stato = ?1 WHERE id = ?2`).bind(stato, id).run();
  } else if (f.get("azione") === "email" && id) {
    // rimanda l'email di avviso (utile dopo aver sistemato le chiavi Mailjet) e salva il nuovo esito
    const c = await env.VISITE.prepare(`SELECT * FROM clandestini WHERE id = ?1`).bind(id).first();
    if (c) {
      const foto = await env.VISITE.prepare(`SELECT tipo, dati FROM clandestini_foto WHERE id = ?1`).bind(id).first();
      const esito = await avvisa(env, new URL(request.url).origin, {
        ...c, coordinate: c.lat != null, foto: foto ? { tipo: foto.tipo, dati: new Uint8Array(foto.dati) } : null,
      });
      await env.VISITE.prepare(`UPDATE clandestini SET email = ?1 WHERE id = ?2`).bind(esito.slice(0, 500), id).run();
    }
  }
  // dopo il clic torna alla pagina (POST -> GET, così il "ricarica" non ripete l'azione)
  return Response.redirect(new URL("/clandestini-admin?chiave=" + encodeURIComponent(chiave), request.url).href, 303);
}

export async function onRequestGet({ request, env }) {
  const chiave = new URL(request.url).searchParams.get("chiave") || "";
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY)) return nonTrovata();

  let corpo;
  if (!env.VISITE) {
    corpo = `<p class="vuoto">Database non collegato: manca il binding D1 <b>VISITE</b> nel progetto Pages (anche nell'ambiente Preview).</p>`;
  } else {
    await preparaClandestini(env.VISITE);
    const oggi = giornoRoma();
    const { results } = await env.VISITE.prepare(
      `SELECT c.*, f.id IS NOT NULL AS foto FROM clandestini c LEFT JOIN clandestini_foto f ON f.id = c.id
       WHERE c.giorno >= ?1 ORDER BY c.giorno, c.ora LIMIT 300`).bind(oggi).all();
    const dataIt = (g) => new Date(g + "T12:00:00Z").toLocaleDateString("it-IT", { weekday: "short", day: "numeric", month: "long", timeZone: "UTC" });
    const pulsante = (r, azione, testo, cls = "") =>
      `<form method="post"><input type="hidden" name="chiave" value="${esc(chiave)}"><input type="hidden" name="id" value="${r.id}">` +
      `<button name="azione" value="${azione}" class="${cls}">${testo}</button></form>`;
    const scheda = (r) => `<article>
      ${r.foto ? `<a href="/api/clandestini-foto/${r.id}?chiave=${encodeURIComponent(chiave)}" target="_blank"><img class="foto" src="/api/clandestini-foto/${r.id}?chiave=${encodeURIComponent(chiave)}" alt="Foto proposta" loading="lazy"></a>` : ""}
      <div class="quando">${esc(dataIt(r.giorno))}${r.ora ? " · ore " + esc(r.ora) : ""}</div>
      <h3>${esc(r.nome)}</h3>
      <div>${esc(r.citta)} · ${esc(r.luogo)} ${r.lat != null
        ? `<a href="https://www.google.com/maps?q=${r.lat},${r.lng}" target="_blank" rel="noopener">coordinate trovate</a>`
        : `<span class="avviso">senza coordinate (non compare in "Vicino a me")</span>`}</div>
      ${r.stili ? `<div class="m">Stili: ${esc(r.stili)}${r.gratis ? " · gratis" : ""}</div>` : r.gratis ? `<div class="m">Gratis</div>` : ""}
      ${r.dettagli ? `<div class="m">Dettagli: ${esc(r.dettagli)}</div>` : ""}
      ${r.link ? `<div class="m">Link: <a href="${esc(r.link)}" target="_blank" rel="noopener noreferrer">${esc(r.link)}</a></div>` : ""}
      ${r.contatto ? `<div class="m">Contatto (privato): ${esc(r.contatto)}</div>` : ""}
      <div class="m piccolo">Proposto il ${esc(new Date(r.creato).toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}
        · ${!r.email ? "✉️ email: esito non registrato"
          : /^inviata/.test(r.email) ? "✉️ email " + esc(r.email)
          : `<span class="avviso">✉️ email ${esc(r.email)}</span>`}</div>
      <div class="azioni">${r.stato === "attesa" ? pulsante(r, "approva", "✓ Approva", "si") + pulsante(r, "rifiuta", "✕ Rifiuta")
        : r.stato === "ok" ? pulsante(r, "togli", "Togli dal sito") : pulsante(r, "riapri", "Rimetti in attesa")}
        ${/^inviata/.test(r.email || "") ? "" : pulsante(r, "email", "✉️ Rimanda email")}</div>
    </article>`;
    const gruppo = (titolo, righe, vuoto) =>
      `<section><h2>${titolo} <span class="n">${righe.length}</span></h2>${righe.length ? righe.map(scheda).join("") : `<p class="vuoto">${vuoto}</p>`}</section>`;
    corpo = gruppo("Da approvare", results.filter((r) => r.stato === "attesa"), "Nessuna proposta in attesa.") +
      gruppo("Sul sito", results.filter((r) => r.stato === "ok"), "Nessun clandestino in programma.") +
      gruppo("Rifiutati", results.filter((r) => r.stato === "no"), "Nessuno.");
  }

  const html = `<!doctype html><html lang="it"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow">
<title>Clandestini siBalla</title><link rel="icon" href="/logo-app.svg">
<style>
:root{--bg:#0E0E12;--surface:#1B1B22;--surface-2:#26262F;--accent:#FFD400;--muted:#9A9AA8;--text:#F2F2F5}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
main{max-width:720px;margin:0 auto;padding:24px 16px 48px}h1{margin:0 0 4px;font-size:24px}h1 span{color:var(--accent)}
.sub{color:var(--muted);margin:0 0 16px;font-size:13px}a{color:var(--accent);overflow-wrap:anywhere}
section{margin:20px 0}h2{font-size:15px;margin:0 0 8px;color:var(--accent)}h2 .n{color:var(--muted);font-weight:400}
article{background:var(--surface);border-radius:14px;padding:12px 14px;margin:10px 0}h3{margin:2px 0 4px;font-size:16px}
.quando{color:var(--accent);font-weight:600;font-size:13px;text-transform:capitalize}.m{color:var(--muted);font-size:13px;margin-top:3px;overflow-wrap:anywhere}
.foto{display:block;width:100%;max-height:260px;object-fit:cover;border-radius:10px;margin-bottom:8px;background:var(--surface-2)}
.piccolo{font-size:12px}.avviso{color:#FF9F43;font-size:13px}.vuoto{color:var(--muted)}
.azioni{display:flex;gap:8px;margin-top:10px}form{margin:0}
button{font:inherit;font-weight:600;padding:6px 14px;border-radius:9px;border:1px solid rgba(255,255,255,.14);background:var(--surface-2);color:var(--text);cursor:pointer}
button.si{background:var(--accent);border-color:var(--accent);color:var(--bg)}
</style></head><body><main>
<h1>Clandestini <span>siBalla</span></h1>
<p class="sub">Eventi proposti dagli utenti dal sito, da oggi in poi. Quelli approvati compaiono subito nella scheda "Clandestini" (entro un minuto).
· <a href="/statistiche?chiave=${encodeURIComponent(chiave)}">Statistiche delle visite</a></p>
${corpo}
</main></body></html>`;
  return new Response(html, {
    headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" },
  });
}
