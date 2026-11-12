// Bacheca: annunci degli utenti (passaggi, cerco partner), nello stesso database D1 delle visite (binding "VISITE").
// stato: "attesa" (appena inviato, non visibile) -> "ok" (approvato, visibile) oppure "no" (rifiutato o tolto).
// Si approvano dalla pagina nascosta /bacheca-admin?chiave=... (stessa chiave STATS_KEY delle statistiche).
// Ogni annuncio scade da solo: il giorno indicato (es. la data del festival) oppure 30 giorni dopo l'invio.

let tabellaPronta = false;

export const CATEGORIE = {
  passaggio: { nome: "Passaggi", cerco: "Cerco un passaggio", offro: "Offro un passaggio" },
  partner: { nome: "Cerco partner", cerco: "Cerco partner", offro: "Mi propongo come partner" },
};
export const RUOLI = ["leader", "follower", "entrambi"];
export const GIORNI_SCADENZA = 30;

export async function preparaBacheca(db) {
  if (tabellaPronta) return;
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS annunci (
       id INTEGER PRIMARY KEY AUTOINCREMENT,
       creato TEXT NOT NULL,              -- ISO, ora UTC
       stato TEXT NOT NULL DEFAULT 'attesa',
       categoria TEXT NOT NULL,           -- passaggio | partner
       tipo TEXT NOT NULL,                -- cerco | offro
       evento TEXT NOT NULL DEFAULT '',   -- festival, workshop o serata a cui si riferisce
       giorno TEXT NOT NULL DEFAULT '',   -- yyyy-mm-dd, facoltativo (data del viaggio o dell'evento)
       scade TEXT NOT NULL,               -- yyyy-mm-dd: dopo questo giorno l'annuncio sparisce
       citta TEXT NOT NULL,               -- partenza (passaggi) o città (partner)
       arrivo TEXT NOT NULL DEFAULT '',   -- solo passaggi: destinazione
       ruolo TEXT NOT NULL DEFAULT '',    -- solo partner: leader | follower | entrambi
       stile TEXT NOT NULL DEFAULT '',
       testo TEXT NOT NULL DEFAULT '',
       nome TEXT NOT NULL,                -- come firmarsi (nome o soprannome)
       contatto TEXT NOT NULL,            -- PUBBLICO: Instagram, email o telefono, scelto da chi scrive
       email TEXT NOT NULL DEFAULT ''     -- esito dell'email di avviso
     )`).run();
  tabellaPronta = true;
}
