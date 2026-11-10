import { preparaTabella, giornoRoma } from "../lib/visite.js";

// GET /statistiche?chiave=...&giorni=7|30|365|0 : pagina nascosta con le visite per regione e città.
// Senza la chiave giusta risponde 404, come se la pagina non esistesse.

const REGIONI = {
  "Aosta Valley": "Valle d'Aosta", "Piedmont": "Piemonte", "Lombardy": "Lombardia",
  "Trentino-Alto Adige": "Trentino-Alto Adige", "Trentino-South Tyrol": "Trentino-Alto Adige",
  "Friuli Venezia Giulia": "Friuli-Venezia Giulia", "Friuli-Venezia Giulia": "Friuli-Venezia Giulia",
  "Tuscany": "Toscana", "Apulia": "Puglia", "Sicily": "Sicilia", "Sardinia": "Sardegna",
  "Latium": "Lazio", "Lazio": "Lazio", "Campania": "Campania", "Calabria": "Calabria",
  "Basilicata": "Basilicata", "Molise": "Molise", "Abruzzo": "Abruzzo", "Marche": "Marche",
  "The Marches": "Marche", "Umbria": "Umbria", "Emilia-Romagna": "Emilia-Romagna",
  "Veneto": "Veneto", "Liguria": "Liguria",
};
const CITTA = {
  "Genoa": "Genova", "Milan": "Milano", "Turin": "Torino", "Rome": "Roma", "Florence": "Firenze",
  "Naples": "Napoli", "Venice": "Venezia", "Padua": "Padova", "Mantua": "Mantova",
  "Syracuse": "Siracusa", "Leghorn": "Livorno",
};

function nomePaese(codice) {
  try { return new Intl.DisplayNames(["it"], { type: "region" }).of(codice) || codice; } catch { return codice; }
}
function esc(s) {
  return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}
function uguali(a, b) { // confronto a tempo costante
  if (a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}
const nonTrovata = () => new Response("Not found", { status: 404 });

export async function onRequestGet({ request, env }) {
  const url = new URL(request.url);
  const chiave = url.searchParams.get("chiave") || "";
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY)) return nonTrovata();

  const giorni = [7, 30, 365, 0].includes(Number(url.searchParams.get("giorni") ?? 30))
    ? Number(url.searchParams.get("giorni") ?? 30) : 30;
  let corpo;
  if (!env.VISITE) {
    corpo = `<p class="vuoto">Database non collegato: manca il binding D1 <b>VISITE</b> nel progetto Pages.</p>`;
  } else {
    await preparaTabella(env.VISITE);
    const dal = giorni ? giornoRoma(new Date(Date.now() - (giorni - 1) * 86400000)) : "0000-00-00";
    const q = (sql) => env.VISITE.prepare(sql).bind(dal).all().then((r) => r.results);
    const [totale, perGiorno, regioni, citta, paesi, primo] = await Promise.all([
      q(`SELECT COALESCE(SUM(n),0) AS n FROM visite WHERE giorno >= ?1`),
      q(`SELECT giorno, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY giorno ORDER BY giorno DESC LIMIT 60`),
      q(`SELECT paese, regione, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, regione, citta, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione, citta ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese ORDER BY n DESC LIMIT 30`),
      env.VISITE.prepare(`SELECT MIN(giorno) AS g FROM visite`).all().then((r) => r.results),
    ]);
    const tot = totale[0]?.n || 0;
    const estero = (p) => (p && p !== "IT" && p !== "?" ? ` <span class="paese">(${esc(nomePaese(p))})</span>` : "");
    const regione = (r) => (r.regione === "?" ? "sconosciuta" : esc(REGIONI[r.regione] || r.regione)) + estero(r.paese);
    const tabella = (titolo, righe, etichetta) => {
      if (!righe.length) return "";
      const max = Math.max(...righe.map((r) => r.n));
      return `<section><h2>${titolo}</h2><table>${righe.map((r) =>
        `<tr><td>${etichetta(r)}</td><td class="num">${r.n}</td><td class="barra"><span style="width:${Math.max(2, (r.n / max) * 100)}%"></span></td></tr>`
      ).join("")}</table></section>`;
    };
    const dataIt = (g) => g.split("-").reverse().join("/");
    corpo = `
      <p class="totale"><b>${tot}</b> visite ${giorni ? `negli ultimi ${giorni} giorni` : "in totale"}${primo[0]?.g ? ` · conteggio attivo dal ${dataIt(primo[0].g)}` : ""}</p>
      ${tot ? "" : `<p class="vuoto">Nessuna visita registrata nel periodo.</p>`}
      ${tabella("Regioni", regioni, regione)}
      ${tabella("Città", citta, (r) => (r.citta === "?" ? "sconosciuta" : esc(CITTA[r.citta] || r.citta)) + ` <span class="paese">· ${regione(r)}</span>`)}
      ${tabella("Paesi", paesi, (r) => (r.paese === "?" ? "sconosciuto" : esc(nomePaese(r.paese))))}
      ${tabella("Giorni", perGiorno, (r) => dataIt(r.giorno))}`;
  }

  const link = (g, testo) =>
    `<a class="${g === giorni ? "attivo" : ""}" href="?chiave=${encodeURIComponent(chiave)}&giorni=${g}">${testo}</a>`;
  const html = `<!doctype html><html lang="it"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow">
<title>Statistiche siBalla</title><link rel="icon" href="/logo-app.svg">
<style>
:root{--bg:#0E0E12;--surface:#1B1B22;--surface-2:#26262F;--accent:#FFD400;--muted:#9A9AA8;--text:#F2F2F5}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
main{max-width:720px;margin:0 auto;padding:24px 16px 48px}h1{margin:0 0 4px;font-size:24px}h1 span{color:var(--accent)}
.sub{color:var(--muted);margin:0 0 16px;font-size:13px}
nav{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px}nav a{padding:6px 12px;border-radius:999px;background:var(--surface-2);color:var(--text);text-decoration:none;font-size:13px}
nav a.attivo{background:var(--accent);color:var(--bg);font-weight:600}
.totale{font-size:16px}.totale b{color:var(--accent);font-size:28px}.vuoto{color:var(--muted)}
section{background:var(--surface);border-radius:14px;padding:12px 14px;margin:14px 0}h2{font-size:15px;margin:0 0 8px;color:var(--accent)}
table{width:100%;border-collapse:collapse}td{padding:5px 4px;border-top:1px solid var(--surface-2);vertical-align:middle}tr:first-child td{border-top:0}
.num{text-align:right;font-variant-numeric:tabular-nums;width:3.5em;font-weight:600}.barra{width:35%}
.barra span{display:block;height:8px;border-radius:4px;background:var(--accent);opacity:.8}.paese{color:var(--muted);font-size:13px}
</style></head><body><main>
<h1>Statistiche <span>siBalla</span></h1>
<p class="sub">Visite anonime per luogo (approssimativo, ricavato dall'IP: la città può essere imprecisa). Aggiornato al ${esc(new Date().toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}.</p>
<nav>${link(7, "7 giorni")}${link(30, "30 giorni")}${link(365, "1 anno")}${link(0, "Sempre")}</nav>
${corpo}
</main></body></html>`;
  return new Response(html, {
    headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" },
  });
}
