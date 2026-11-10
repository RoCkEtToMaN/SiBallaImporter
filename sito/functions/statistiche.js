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
    const s = (tipo, limite) => q(`SELECT chiave, SUM(n) AS n FROM statistiche WHERE giorno >= ?1 AND tipo = '${tipo}'
      GROUP BY chiave ORDER BY n DESC LIMIT ${limite}`);
    const [totale, perGiorno, regioni, citta, paesi, primo, visitatori, pagine, fonti, disp, click, mesi, primoStat] = await Promise.all([
      q(`SELECT COALESCE(SUM(n),0) AS n FROM visite WHERE giorno >= ?1`),
      q(`SELECT v.giorno, SUM(v.n) AS n, (SELECT n FROM statistiche s WHERE s.giorno = v.giorno AND tipo = 'visitatori' AND chiave = 'giorno') AS u
         FROM visite v WHERE v.giorno >= ?1 GROUP BY v.giorno ORDER BY v.giorno DESC LIMIT 60`),
      q(`SELECT paese, regione, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, regione, citta, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione, citta ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese ORDER BY n DESC LIMIT 30`),
      env.VISITE.prepare(`SELECT MIN(giorno) AS g FROM visite`).all().then((r) => r.results),
      s("visitatori", 5), s("pagina", 20), s("fonte", 20), s("disp", 5), s("click", 40),
      q(`SELECT substr(giorno,1,7) AS mese, SUM(CASE WHEN tipo='pagina' THEN n ELSE 0 END) AS viste,
           SUM(CASE WHEN tipo='visitatori' AND chiave='mese' THEN n ELSE 0 END) AS u
         FROM statistiche WHERE giorno >= ?1 GROUP BY mese ORDER BY mese DESC LIMIT 24`),
      env.VISITE.prepare(`SELECT MIN(giorno) AS g FROM statistiche`).all().then((r) => r.results),
    ]);
    const tot = totale[0]?.n || 0;
    const vis = Object.fromEntries(visitatori.map((r) => [r.chiave, r.n]));
    // media sui giorni del periodo da quando esistono questi dati (oggi compreso)
    const inizio = primoStat[0]?.g && primoStat[0].g > dal ? primoStat[0].g : dal;
    const nGiorni = primoStat[0]?.g ? Math.max(1, Math.round((Date.parse(giornoRoma()) - Date.parse(inizio)) / 86400000) + 1) : 1;
    const mediaGiorno = Math.round(((vis.giorno || 0) / nGiorni) * 10) / 10;
    const NOMI_MESI = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];
    const nomeMese = (m) => NOMI_MESI[Number(m.slice(5, 7)) - 1] + " " + m.slice(0, 4);
    const estero = (p) => (p && p !== "IT" && p !== "?" ? ` <span class="paese">(${esc(nomePaese(p))})</span>` : "");
    const regione = (r) => (r.regione === "?" ? "sconosciuta" : esc(REGIONI[r.regione] || r.regione)) + estero(r.paese);
    const tabella = (titolo, righe, etichetta, campo = "n") => {
      if (!righe.length) return "";
      const max = Math.max(...righe.map((r) => r[campo]));
      return `<section><h2>${titolo}</h2><table>${righe.map((r) =>
        `<tr><td>${etichetta(r)}</td><td class="num">${r[campo]}</td><td class="barra"><span style="width:${Math.max(2, (r[campo] / max) * 100)}%"></span></td></tr>`
      ).join("")}</table></section>`;
    };
    const dataIt = (g) => g.split("-").reverse().join("/");
    corpo = `
      <p class="totale"><b>${tot}</b> visite ${giorni ? `negli ultimi ${giorni} giorni` : "in totale"}${primo[0]?.g ? ` · conteggio attivo dal ${dataIt(primo[0].g)}` : ""}</p>
      ${primoStat[0]?.g ? `<p class="dettaglio"><b>${mediaGiorno}</b> visitatori unici al giorno in media · <b>${vis.nuovi || 0}</b> nuovi visitatori
        <br><span class="paese">Visitatori, pagine, provenienza, dispositivi e click registrati dal ${dataIt(primoStat[0].g)}.</span></p>` : ""}
      ${tot ? "" : `<p class="vuoto">Nessuna visita registrata nel periodo.</p>`}
      ${tabella("Mesi · visitatori unici", mesi.filter((r) => r.u), (r) => `${nomeMese(r.mese)} <span class="paese">· ${r.viste} pagine viste</span>`, "u")}
      ${tabella("Pagine più viste", pagine, (r) => esc(r.chiave === "/" ? "/ (eventi)" : r.chiave))}
      ${tabella("Da dove arrivano", fonti, (r) => esc(r.chiave))}
      ${tabella("Dispositivi", disp, (r) => esc(r.chiave))}
      ${tabella("Click su eventi e link esterni", click, (r) => esc(r.chiave))}
      ${tabella("Regioni", regioni, regione)}
      ${tabella("Città", citta, (r) => (r.citta === "?" ? "sconosciuta" : esc(CITTA[r.citta] || r.citta)) + ` <span class="paese">· ${regione(r)}</span>`)}
      ${tabella("Paesi", paesi, (r) => (r.paese === "?" ? "sconosciuto" : esc(nomePaese(r.paese))))}
      ${tabella("Giorni · pagine viste", perGiorno, (r) => dataIt(r.giorno) + (r.u ? ` <span class="paese">· ${r.u} visitatori</span>` : ""))}`;
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
.disp{background:var(--surface);border-radius:12px;padding:10px 14px;font-size:13px;color:var(--muted)}
.disp button{margin-left:6px;padding:4px 10px;border:0;border-radius:999px;background:var(--surface-2);color:var(--text);font:inherit;cursor:pointer}
.dettaglio{margin-top:-6px}.dettaglio b{color:var(--accent)}
.totale{font-size:16px}.totale b{color:var(--accent);font-size:28px}.vuoto{color:var(--muted)}
section{background:var(--surface);border-radius:14px;padding:12px 14px;margin:14px 0}h2{font-size:15px;margin:0 0 8px;color:var(--accent)}
table{width:100%;border-collapse:collapse}td{padding:5px 4px;border-top:1px solid var(--surface-2);vertical-align:middle}tr:first-child td{border-top:0}
.num{text-align:right;font-variant-numeric:tabular-nums;width:3.5em;font-weight:600}.barra{width:35%}
.barra span{display:block;height:8px;border-radius:4px;background:var(--accent);opacity:.8}.paese{color:var(--muted);font-size:13px}
</style></head><body><main>
<h1>Statistiche <span>siBalla</span></h1>
<p class="sub">Visite anonime per luogo (approssimativo, ricavato dall'IP: la città può essere imprecisa). Aggiornato al ${esc(new Date().toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}.</p>
<nav>${link(7, "7 giorni")}${link(30, "30 giorni")}${link(365, "1 anno")}${link(0, "Sempre")}</nav>
<p class="disp" id="disp"></p>
${corpo}
</main>
<script>
// aprire questa pagina esclude il browser dal conteggio (stessa chiave letta da conta.js); il pulsante lo riattiva
(function () {
  var el = document.getElementById("disp"), K = "siballa_noconta";
  function stato() {
    var escluso = false;
    try { escluso = localStorage.getItem(K) === "1"; } catch (e) {}
    el.innerHTML = escluso
      ? "✓ Le tue visite da questo browser non vengono contate. <button id='tg'>Riattiva il conteggio</button>"
      : "Le visite da questo browser vengono contate. <button id='tg'>Escludi questo browser</button>";
    document.getElementById("tg").onclick = function () {
      try { if (escluso) localStorage.removeItem(K); else localStorage.setItem(K, "1"); } catch (e) {}
      stato();
    };
  }
  try { if (!sessionStorage.getItem("siballa_stat_visto")) { localStorage.setItem(K, "1"); sessionStorage.setItem("siballa_stat_visto", "1"); } } catch (e) {}
  stato();
})();
</script>
</body></html>`;
  return new Response(html, {
    headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" },
  });
}
