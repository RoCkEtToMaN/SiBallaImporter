// Contatore visite anonimo: solo giorno + paese/regione/città approssimativi (dall'IP, calcolati da Cloudflare)
// e, nella tabella "statistiche", contatori aggregati (pagina, provenienza, dispositivo, click, visitatori unici).
// Nessun IP, cookie o identificativo viene salvato: i visitatori unici li segnala il browser stesso
// (conta.js ricorda in localStorage solo la data dell'ultima visita).
// Richiede nel progetto Pages: binding D1 "VISITE" e secret "STATS_KEY" (vedi documento di configurazione).

let tabellaPronta = false;

export async function preparaTabella(db) {
  if (tabellaPronta) return;
  await db.batch([
    db.prepare(
      `CREATE TABLE IF NOT EXISTS visite (
         giorno TEXT NOT NULL, paese TEXT NOT NULL, regione TEXT NOT NULL, citta TEXT NOT NULL,
         n INTEGER NOT NULL DEFAULT 0,
         PRIMARY KEY (giorno, paese, regione, citta))`),
    // tipo: pagina | fonte | disp | click | visitatori (chiave: giorno = primo accesso del giorno,
    // mese = primo accesso del mese, nuovi = primo accesso in assoluto)
    db.prepare(
      `CREATE TABLE IF NOT EXISTS statistiche (
         giorno TEXT NOT NULL, tipo TEXT NOT NULL, chiave TEXT NOT NULL,
         n INTEGER NOT NULL DEFAULT 0,
         PRIMARY KEY (giorno, tipo, chiave))`),
  ]);
  tabellaPronta = true;
}

// giorno in ora italiana, "yyyy-mm-dd"
export function giornoRoma(data = new Date()) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Rome" }).format(data);
}
