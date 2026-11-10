// Contatore visite anonimo: solo giorno + paese/regione/città approssimativi (dall'IP, calcolati da Cloudflare).
// Nessun IP, cookie o identificativo viene salvato.
// Richiede nel progetto Pages: binding D1 "VISITE" e secret "STATS_KEY" (vedi documento di configurazione).

let tabellaPronta = false;

export async function preparaTabella(db) {
  if (tabellaPronta) return;
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS visite (
       giorno TEXT NOT NULL, paese TEXT NOT NULL, regione TEXT NOT NULL, citta TEXT NOT NULL,
       n INTEGER NOT NULL DEFAULT 0,
       PRIMARY KEY (giorno, paese, regione, citta))`
  ).run();
  tabellaPronta = true;
}

// giorno in ora italiana, "yyyy-mm-dd"
export function giornoRoma(data = new Date()) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Rome" }).format(data);
}
