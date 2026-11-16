import { preparaTabella, giornoRoma } from "../lib/visite.js";
import { OPZIONI, risultati } from "../lib/sondaggio.js";

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

// POST /statistiche?chiave=... : pulizia a mano dalla pagina.
//   azione=togli      sposta in visite_tolte le visite di un luogo (paese/regione/città) tra "dal" e "al"
//   azione=ripristina rimette in visite tutte le visite di quel luogo che sono nel cestino
// Tocca solo il conteggio per luogo; pagine, provenienza, dispositivi e click non sono collegati al luogo.
export async function onRequestPost({ request, env }) {
  const url = new URL(request.url);
  const chiave = url.searchParams.get("chiave") || "";
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY) || !env.VISITE) return nonTrovata();
  const f = await request.formData();
  const campo = (k) => String(f.get(k) || "").slice(0, 100);
  const [paese, regione, citta] = [campo("paese"), campo("regione"), campo("citta")];
  const data = (k, def) => (/^\d{4}-\d{2}-\d{2}$/.test(campo(k)) ? campo(k) : def);
  const db = env.VISITE;
  await preparaTabella(db);
  let fatto = "";
  if (paese && regione && citta) {
    const dove = `paese = ?1 AND regione = ?2 AND citta = ?3`;
    if (campo("azione") === "togli") {
      const dal = data("dal", "0000-00-00"), al = data("al", "9999-12-31");
      const ora = new Date().toISOString();
      await db.batch([
        db.prepare(`INSERT INTO visite_tolte (giorno, paese, regione, citta, n, tolte_il)
                    SELECT giorno, paese, regione, citta, n, ?6 FROM visite WHERE ${dove} AND giorno BETWEEN ?4 AND ?5
                    ON CONFLICT (giorno, paese, regione, citta) DO UPDATE SET n = n + excluded.n, tolte_il = excluded.tolte_il`)
          .bind(paese, regione, citta, dal, al, ora),
        db.prepare(`DELETE FROM visite WHERE ${dove} AND giorno BETWEEN ?4 AND ?5`).bind(paese, regione, citta, dal, al),
      ]);
      fatto = "tolte";
    } else if (campo("azione") === "ripristina") {
      await db.batch([
        db.prepare(`INSERT INTO visite (giorno, paese, regione, citta, n)
                    SELECT giorno, paese, regione, citta, n FROM visite_tolte WHERE ${dove}
                    ON CONFLICT (giorno, paese, regione, citta) DO UPDATE SET n = n + excluded.n`)
          .bind(paese, regione, citta),
        db.prepare(`DELETE FROM visite_tolte WHERE ${dove}`).bind(paese, regione, citta),
      ]);
      fatto = "ripristinate";
    }
  }
  // torna alla pagina (stesso periodo) con il messaggio; 303 = dopo un POST si ricarica con GET
  url.searchParams.set("fatto", fatto);
  return new Response(null, { status: 303, headers: { Location: url.pathname + url.search, "Cache-Control": "no-store" } });
}

export async function onRequestGet({ request, env }) {
  const url = new URL(request.url);
  const chiave = url.searchParams.get("chiave") || "";
  if (!env.STATS_KEY || !uguali(chiave, env.STATS_KEY)) return nonTrovata();

  // ?giorno=yyyy-mm-dd mostra un solo giorno (si apre cliccando una riga della tabella "Giorni")
  const giornoSolo = /^\d{4}-\d{2}-\d{2}$/.test(url.searchParams.get("giorno") || "") ? url.searchParams.get("giorno") : "";
  const giorni = giornoSolo ? -1 : [7, 30, 365, 0].includes(Number(url.searchParams.get("giorni") ?? 30))
    ? Number(url.searchParams.get("giorni") ?? 30) : 30;
  const qui = `?chiave=${encodeURIComponent(chiave)}` + (giornoSolo ? `&giorno=${giornoSolo}` : `&giorni=${giorni}`);
  const msg = { tolte: "Visite spostate nel cestino (in fondo alla pagina si possono ripristinare).", ripristinate: "Visite ripristinate." }[url.searchParams.get("fatto")] || "";
  let corpo;
  if (!env.VISITE) {
    corpo = `<p class="vuoto">Database non collegato: manca il binding D1 <b>VISITE</b> nel progetto Pages.</p>`;
  } else {
    await preparaTabella(env.VISITE);
    const dal = giornoSolo || (giorni ? giornoRoma(new Date(Date.now() - (giorni - 1) * 86400000)) : "0000-00-00");
    const al = giornoSolo || "9999-12-31";
    // nelle query "giorno >= ?1" diventa "giorno BETWEEN ?1 AND ?2" (periodo o giorno singolo)
    const q = (sql) => env.VISITE.prepare(sql.replace(/giorno >= \?1/g, "giorno BETWEEN ?1 AND ?2")).bind(dal, al).all().then((r) => r.results);
    const s = (tipo, limite) => q(`SELECT chiave, SUM(n) AS n FROM statistiche WHERE giorno >= ?1 AND tipo = '${tipo}'
      GROUP BY chiave ORDER BY n DESC LIMIT ${limite}`);
    const cestino = await env.VISITE.prepare(
      `SELECT paese, regione, citta, SUM(n) AS n, MIN(giorno) AS dal, MAX(giorno) AS al FROM visite_tolte
       GROUP BY paese, regione, citta ORDER BY MAX(tolte_il) DESC LIMIT 40`).all().then((r) => r.results);
    const [totale, perGiorno, regioni, citta, paesi, primo, visitatori, pagine, fonti, disp, click, reti, bot, mesi, primoStat] = await Promise.all([
      q(`SELECT COALESCE(SUM(n),0) AS n FROM visite WHERE giorno >= ?1`),
      q(`SELECT v.giorno, SUM(v.n) AS n, (SELECT n FROM statistiche s WHERE s.giorno = v.giorno AND tipo = 'visitatori' AND chiave = 'giorno') AS u
         FROM visite v WHERE v.giorno >= ?1 GROUP BY v.giorno ORDER BY v.giorno DESC LIMIT 60`),
      q(`SELECT paese, regione, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, regione, citta, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese, regione, citta ORDER BY n DESC LIMIT 40`),
      q(`SELECT paese, SUM(n) AS n FROM visite WHERE giorno >= ?1 GROUP BY paese ORDER BY n DESC LIMIT 30`),
      env.VISITE.prepare(`SELECT MIN(giorno) AS g FROM visite`).all().then((r) => r.results),
      s("visitatori", 5), s("pagina", 20), s("fonte", 20), s("disp", 5), s("click", 40), s("rete", 15), s("bot", 20),
      q(`SELECT substr(giorno,1,7) AS mese, SUM(CASE WHEN tipo='pagina' THEN n ELSE 0 END) AS viste,
           SUM(CASE WHEN tipo='visitatori' AND chiave='mese' THEN n ELSE 0 END) AS u
         FROM statistiche WHERE giorno >= ?1 GROUP BY mese ORDER BY mese DESC LIMIT 24`),
      env.VISITE.prepare(`SELECT MIN(giorno) AS g FROM statistiche`).all().then((r) => r.results),
    ]);
    // sondaggio "Quali rubriche vorresti?" (pagina /rubriche): non dipende dal periodo scelto
    const sond = await risultati(env.VISITE, true);
    const tot = totale[0]?.n || 0;
    const vis = Object.fromEntries(visitatori.map((r) => [r.chiave, r.n]));
    // media sui giorni del periodo da quando esistono questi dati (oggi compreso)
    const inizio = primoStat[0]?.g && primoStat[0].g > dal ? primoStat[0].g : dal;
    const nGiorni = primoStat[0]?.g ? Math.max(1, Math.round((Date.parse(giornoSolo || giornoRoma()) - Date.parse(inizio)) / 86400000) + 1) : 1;
    const mediaGiorno = Math.round(((vis.giorno || 0) / nGiorni) * 10) / 10;
    const NOMI_MESI = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];
    const nomeMese = (m) => NOMI_MESI[Number(m.slice(5, 7)) - 1] + " " + m.slice(0, 4);
    const estero = (p) => (p && p !== "IT" && p !== "?" ? ` <span class="paese">(${esc(nomePaese(p))})</span>` : "");
    const regione = (r) => (r.regione === "?" ? "sconosciuta" : esc(REGIONI[r.regione] || r.regione)) + estero(r.paese);
    const tabella = (titolo, righe, etichetta, campo = "n", extra = null, nota = "") => {
      if (!righe.length) return "";
      const max = Math.max(...righe.map((r) => r[campo]));
      return `<section><h2>${titolo}</h2>${nota}<table>${righe.map((r) =>
        `<tr><td>${etichetta(r)}</td><td class="num">${r[campo]}</td><td class="barra"><span style="width:${Math.max(2, (r[campo] / max) * 100)}%"></span></td>${extra ? `<td class="az">${extra(r)}</td>` : ""}</tr>`
      ).join("")}</table></section>`;
    };
    const dataIt = (g) => g.split("-").reverse().join("/");
    const nomeCitta = (r) => (r.citta === "?" ? "sconosciuta" : esc(CITTA[r.citta] || r.citta)) + ` <span class="paese">· ${regione(r)}</span>`;
    const luogo = (r) => (r.citta === "?" ? "città sconosciuta" : CITTA[r.citta] || r.citta) + ", " +
      (r.regione === "?" ? "regione sconosciuta" : REGIONI[r.regione] || r.regione) + (r.paese !== "IT" ? ` (${nomePaese(r.paese)})` : "");
    const periodo = giornoSolo ? `il ${dataIt(giornoSolo)}` : giorni ? `negli ultimi ${giorni} giorni` : "da sempre";
    // pulsante ✕ (sposta nel cestino) e ↺ (ripristina): POST a questa stessa pagina
    const bottone = (azione, r, simbolo, domanda) =>
      `<form method="post" action="${esc(qui)}" onsubmit="return confirm(${esc(JSON.stringify(domanda))})">` +
      `<input type="hidden" name="azione" value="${azione}"><input type="hidden" name="paese" value="${esc(r.paese)}">` +
      `<input type="hidden" name="regione" value="${esc(r.regione)}"><input type="hidden" name="citta" value="${esc(r.citta)}">` +
      `<input type="hidden" name="dal" value="${dal}"><input type="hidden" name="al" value="${al}">` +
      `<button title="${azione === "togli" ? "Togli queste visite (finiscono nel cestino)" : "Ripristina"}">${simbolo}</button></form>`;
    const togli = (r) => bottone("togli", r, "✕", `Togliere le ${r.n} visite da ${luogo(r)} ${periodo}?\nFiniscono nel cestino e si possono ripristinare.`);
    const ripristina = (r) => bottone("ripristina", r, "↺", `Ripristinare le ${r.n} visite da ${luogo(r)}?`);
    corpo = `
      ${msg ? `<p class="ok">${msg}</p>` : ""}
      <p class="totale"><b>${tot}</b> visite ${periodo}${primo[0]?.g ? ` · conteggio attivo dal ${dataIt(primo[0].g)}` : ""}</p>
      ${primoStat[0]?.g ? `<p class="dettaglio"><b>${mediaGiorno}</b> visitatori unici al giorno in media · <b>${vis.nuovi || 0}</b> nuovi visitatori
        <br><span class="paese">Visitatori, pagine, provenienza, dispositivi e click registrati dal ${dataIt(primoStat[0].g)}.</span></p>` : ""}
      ${tot ? "" : `<p class="vuoto">Nessuna visita registrata nel periodo.</p>`}
      ${tabella("Mesi · visitatori unici", mesi.filter((r) => r.u), (r) => `${nomeMese(r.mese)} <span class="paese">· ${r.viste} pagine viste</span>`, "u")}
      ${tabella("Pagine più viste", pagine, (r) => esc(r.chiave === "/" ? "/ (eventi)" : r.chiave))}
      ${tabella("Da dove arrivano", fonti, (r) => esc(r.chiave))}
      ${tabella("Dispositivi", disp, (r) => esc(r.chiave))}
      ${tabella("Click su eventi e link esterni", click, (r) => esc(r.chiave))}
      ${sond.totale ? tabella(`Sondaggio rubriche · ${sond.totale} ${sond.totale === 1 ? "voto" : "voti"} (da sempre)`,
        OPZIONI.map(([k, t]) => ({ t, n: sond.conta[k] })).sort((a, b) => b.n - a.n), (r) => esc(r.t)) +
        (sond.proposte.length ? `<section><h2>Rubriche proposte</h2><table>${sond.proposte.map((r) =>
          `<tr><td>${esc(r.proposta)} <span class="paese">· ${dataIt(r.aggiornato.slice(0, 10))}</span></td></tr>`).join("")}</table></section>` : "") : ""}
      ${tabella("Regioni", regioni, regione)}
      ${tabella("Città", citta, nomeCitta, "n", togli,
        `<p class="nota">✕ toglie le visite di quel luogo ${periodo} (bot, visite tue). Per un solo giorno, apri il giorno nella tabella “Giorni”.</p>`)}
      ${tabella("Paesi", paesi, (r) => (r.paese === "?" ? "sconosciuto" : esc(nomePaese(r.paese))))}
      ${tabella("Reti dei visitatori (operatore internet)", reti, (r) => esc(r.chiave))}
      ${tabella(`Probabili bot esclusi · ${bot.reduce((t, r) => t + r.n, 0)} visite non contate (reti di data center)`, bot, (r) => esc(r.chiave))}
      ${tabella("Giorni · pagine viste", perGiorno, (r) => `<a href="?chiave=${encodeURIComponent(chiave)}&giorno=${r.giorno}">${dataIt(r.giorno)}</a>` + (r.u ? ` <span class="paese">· ${r.u} visitatori</span>` : ""))}
      ${tabella("Cestino · visite tolte a mano", cestino,
        (r) => nomeCitta(r) + ` <span class="paese">· ${r.dal === r.al ? dataIt(r.dal) : `${dataIt(r.dal)} → ${dataIt(r.al)}`}</span>`, "n", ripristina,
        `<p class="nota">Non contano nelle statistiche. ↺ le rimette al loro posto.</p>`)}`;
  }

  const link = (g, testo) =>
    `<a class="${g === giorni ? "attivo" : ""}" href="?chiave=${encodeURIComponent(chiave)}&giorni=${g}">${testo}</a>`;
  const chipGiorno = giornoSolo ? `<a class="attivo" href="${esc(qui)}">${giornoSolo.split("-").reverse().join("/")}</a>` : "";
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
.az{width:2.2em;text-align:right}.az form{margin:0}.az button{border:0;border-radius:999px;width:26px;height:26px;background:var(--surface-2);color:var(--muted);cursor:pointer;font:inherit}
.az button:hover{color:var(--bg);background:var(--accent)}.nota{color:var(--muted);font-size:12px;margin:-4px 0 8px}
.ok{background:#1f3a24;color:#b8f0c2;border-radius:12px;padding:8px 14px;font-size:13px}td a{color:var(--text)}
.dettaglio{margin-top:-6px}.dettaglio b{color:var(--accent)}
.totale{font-size:16px}.totale b{color:var(--accent);font-size:28px}.vuoto{color:var(--muted)}
section{background:var(--surface);border-radius:14px;padding:12px 14px;margin:14px 0}h2{font-size:15px;margin:0 0 8px;color:var(--accent)}
table{width:100%;border-collapse:collapse}td{padding:5px 4px;border-top:1px solid var(--surface-2);vertical-align:middle}tr:first-child td{border-top:0}
.num{text-align:right;font-variant-numeric:tabular-nums;width:3.5em;font-weight:600}.barra{width:35%}
.barra span{display:block;height:8px;border-radius:4px;background:var(--accent);opacity:.8}.paese{color:var(--muted);font-size:13px}
</style></head><body><main>
<nav><a href="/community-admin?chiave=${encodeURIComponent(chiave)}">Gestisci le community</a></nav>
<h1>Statistiche <span>siBalla</span></h1>
<p class="sub">Visite anonime per luogo (approssimativo, ricavato dall'IP: la città può essere imprecisa). Aggiornato al ${esc(new Date().toLocaleString("it-IT", { timeZone: "Europe/Rome" }))}.</p>
<nav>${link(7, "7 giorni")}${link(30, "30 giorni")}${link(365, "1 anno")}${link(0, "Sempre")}${chipGiorno}</nav>
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
