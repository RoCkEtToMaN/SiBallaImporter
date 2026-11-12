import { giornoRoma } from "../lib/visite.js";
import { esc, uguali, dataSheet } from "../lib/clandestini.js";
import { preparaBacheca, CATEGORIE, SOGLIA_SEGNALAZIONI, avvisaAutore } from "../lib/bacheca.js";

// GET  /bacheca-admin?chiave=... : pagina nascosta con gli annunci della bacheca, da approvare o rifiutare.
// POST /bacheca-admin            : pulsanti Approva / Rifiuta / Togli / Rimetti in attesa.
// Stessa chiave delle statistiche (secret STATS_KEY); senza la chiave giusta risponde 404.

const nonTrovata = () => new Response("Not found", { status: 404 });
const AZIONI = { approva: "ok", rifiuta: "no", togli: "no", riapri: "attesa" };

export async function onRequestPost({ request, env }) {
  const f = await request.formData();
  const chiave = String(f.get("chiave") || "");
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY) || !env.VISITE) return nonTrovata();
  const stato = AZIONI[f.get("azione")], id = Number(f.get("id"));
  await preparaBacheca(env.VISITE);
  if (stato && id) {
    const prima = await env.VISITE.prepare(`SELECT * FROM annunci WHERE id = ?1`).bind(id).first();
    // tolto o rifiutato: l'email privata non serve più
    await env.VISITE.prepare(`UPDATE annunci SET stato = ?1, avvisi = CASE WHEN ?2 = 1 THEN '' ELSE avvisi END WHERE id = ?3`)
      .bind(stato, stato === "no" ? 1 : 0, id).run();
    // appena approvato: avviso a chi l'ha scritto, se ha lasciato l'email privata
    if (prima && prima.stato === "attesa" && stato === "ok") {
      try {
        await avvisaAutore(env, new URL(request.url).origin, prima, "📌 Il tuo annuncio è in bacheca",
          ["Il tuo annuncio è stato approvato ed è visibile su siBalla.",
            "Ti scriveremo quando qualcuno ti risponde nella discussione sotto l'annuncio."]);
      } catch (e) { console.log("avviso all'autore non inviato: " + e); }
    }
  }
  // messaggi delle discussioni
  else if (f.get("azione") === "cancella_msg" && id) {
    await env.VISITE.prepare(`DELETE FROM messaggi WHERE id = ?1`).bind(id).run();
    await env.VISITE.prepare(`DELETE FROM segnalazioni WHERE messaggio = ?1`).bind(id).run();
  } else if (f.get("azione") === "mostra_msg" && id) {
    await env.VISITE.prepare(`UPDATE messaggi SET nascosto = 0 WHERE id = ?1`).bind(id).run();
    await env.VISITE.prepare(`DELETE FROM segnalazioni WHERE messaggio = ?1`).bind(id).run();
  } else if (f.get("azione") === "nascondi_msg" && id) {
    await env.VISITE.prepare(`UPDATE messaggi SET nascosto = 1 WHERE id = ?1`).bind(id).run();
  }
  return Response.redirect(new URL("/bacheca-admin?chiave=" + encodeURIComponent(chiave), request.url).href, 303);
}

export async function onRequestGet({ request, env }) {
  const chiave = new URL(request.url).searchParams.get("chiave") || "";
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY)) return nonTrovata();

  let corpo;
  if (!env.VISITE) {
    corpo = `<p class="vuoto">Database non collegato: manca il binding D1 <b>VISITE</b> nel progetto Pages (anche nell'ambiente Preview).</p>`;
  } else {
    await preparaBacheca(env.VISITE);
    const { results } = await env.VISITE.prepare(
      `SELECT * FROM annunci WHERE scade >= ?1 ORDER BY id DESC LIMIT 300`).bind(giornoRoma()).all();
    const pulsante = (r, azione, testo, cls = "") =>
      `<form method="post"><input type="hidden" name="chiave" value="${esc(chiave)}"><input type="hidden" name="id" value="${r.id}">` +
      `<button name="azione" value="${azione}" class="${cls}">${testo}</button></form>`;
    const scheda = (r) => `<article>
      <div class="quando">${esc(CATEGORIE[r.categoria]?.[r.tipo] || r.categoria)}${r.giorno ? " · " + esc(dataSheet(r.giorno)) : ""}</div>
      <h3>${esc(r.citta)}${r.arrivo ? " → " + esc(r.arrivo) : ""}${r.evento ? " · " + esc(r.evento) : ""}</h3>
      ${r.ruolo || r.stile ? `<div class="m">${esc([r.ruolo, r.stile].filter(Boolean).join(" · "))}</div>` : ""}
      ${r.testo ? `<div class="m">${esc(r.testo)}</div>` : ""}
      <div class="m">Firma: ${esc(r.nome)} · ${r.contatto ? "contatto pubblico: " + esc(r.contatto) : "nessun contatto (solo discussione)"}
        ${r.avvisi ? " · ✉️ avvisi a " + esc(r.avvisi) + " (privata)" : ""}</div>
      <div class="m piccolo">Inviato il ${esc(new Date(r.creato).toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}
        · scade il ${esc(dataSheet(r.scade))} · ✉️ ${r.email ? esc(r.email) : "esito non registrato"}</div>
      <div class="azioni">${r.stato === "attesa" ? pulsante(r, "approva", "✓ Approva", "si") + pulsante(r, "rifiuta", "✕ Rifiuta")
        : r.stato === "ok" ? pulsante(r, "togli", "Togli dalla bacheca") : pulsante(r, "riapri", "Rimetti in attesa")}</div>
    </article>`;
    const gruppo = (titolo, righe, vuoto) =>
      `<section><h2>${titolo} <span class="n">${righe.length}</span></h2>${righe.length ? righe.map(scheda).join("") : `<p class="vuoto">${vuoto}</p>`}</section>`;
    // ultimi messaggi delle discussioni (compaiono senza approvazione): prima quelli nascosti o segnalati
    const msg = (await env.VISITE.prepare(
      `SELECT m.*, a.citta, a.arrivo, a.evento, (SELECT COUNT(*) FROM segnalazioni s WHERE s.messaggio = m.id) AS segn
       FROM messaggi m JOIN annunci a ON a.id = m.annuncio WHERE a.scade >= ?1
       ORDER BY m.nascosto DESC, segn DESC, m.id DESC LIMIT 150`).bind(giornoRoma()).all()).results;
    const schedaMsg = (m) => `<article class="msg${m.nascosto ? " nascosto" : ""}">
      <div class="quando">${esc(m.citta)}${m.arrivo ? " → " + esc(m.arrivo) : ""}${m.evento ? " · " + esc(m.evento) : ""}</div>
      <div><b>${esc(m.nick)}</b>: ${esc(m.testo)}</div>
      <div class="m piccolo">${esc(new Date(m.creato).toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}
        ${m.segn ? ` · <span class="avviso">⚑ ${m.segn} segnalazion${m.segn === 1 ? "e" : "i"}</span>` : ""}${m.nascosto ? ` · <span class="avviso">nascosto</span>` : ""}</div>
      <div class="azioni">${pulsante(m, "cancella_msg", "Cancella")}${m.nascosto ? pulsante(m, "mostra_msg", "Rendi visibile", "si") : pulsante(m, "nascondi_msg", "Nascondi")}</div>
    </article>`;
    const sezioneMsg = `<section><h2>Messaggi delle discussioni <span class="n">${msg.length}</span></h2>` +
      `<p class="m">Compaiono subito, senza approvazione. Con ${SOGLIA_SEGNALAZIONI} segnalazioni si nascondono da soli e arriva un'email.</p>` +
      (msg.length ? msg.map(schedaMsg).join("") : `<p class="vuoto">Nessun messaggio.</p>`) + `</section>`;
    corpo = gruppo("Da approvare", results.filter((r) => r.stato === "attesa"), "Nessun annuncio in attesa.") +
      gruppo("In bacheca", results.filter((r) => r.stato === "ok"), "Nessun annuncio pubblicato.") +
      sezioneMsg +
      gruppo("Rifiutati o tolti", results.filter((r) => r.stato === "no"), "Nessuno.");
  }

  const html = `<!doctype html><html lang="it"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow">
<title>Bacheca siBalla</title><link rel="icon" href="/logo-app.svg">
<style>
:root{--bg:#0E0E12;--surface:#1B1B22;--surface-2:#26262F;--accent:#FFD400;--muted:#9A9AA8;--text:#F2F2F5}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
main{max-width:720px;margin:0 auto;padding:24px 16px 48px}h1{margin:0 0 4px;font-size:24px}h1 span{color:var(--accent)}
.sub{color:var(--muted);margin:0 0 16px;font-size:13px}a{color:var(--accent);overflow-wrap:anywhere}
section{margin:20px 0}h2{font-size:15px;margin:0 0 8px;color:var(--accent)}h2 .n{color:var(--muted);font-weight:400}
article{background:var(--surface);border-radius:14px;padding:12px 14px;margin:10px 0}h3{margin:2px 0 4px;font-size:16px;overflow-wrap:anywhere}
.quando{color:var(--accent);font-weight:600;font-size:13px}.m{color:var(--muted);font-size:13px;margin-top:3px;overflow-wrap:anywhere}
.piccolo{font-size:12px}.vuoto{color:var(--muted)}.avviso{color:#FF9F43}
article.msg{padding:10px 14px}article.msg.nascosto{opacity:.75;border:1px dashed #FF9F43}.azioni{display:flex;gap:8px;margin-top:10px}form{margin:0}
button{font:inherit;font-weight:600;padding:6px 14px;border-radius:9px;border:1px solid rgba(255,255,255,.14);background:var(--surface-2);color:var(--text);cursor:pointer}
button.si{background:var(--accent);border-color:var(--accent);color:var(--bg)}
</style></head><body><main>
<h1>Bacheca <span>siBalla</span></h1>
<p class="sub">Annunci inviati dagli utenti, non ancora scaduti. Quelli approvati compaiono subito nella pagina Bacheca (entro un minuto).
· <a href="/clandestini-admin?chiave=${encodeURIComponent(chiave)}">Clandestini</a>
· <a href="/statistiche?chiave=${encodeURIComponent(chiave)}">Statistiche delle visite</a></p>
${corpo}
</main></body></html>`;
  return new Response(html, {
    headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" },
  });
}
