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
  // discussione sotto ogni annuncio: messaggi pubblici SENZA approvazione (si nascondono con le segnalazioni)
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS messaggi (
       id INTEGER PRIMARY KEY AUTOINCREMENT,
       annuncio INTEGER NOT NULL,
       creato TEXT NOT NULL,                  -- ISO, ora UTC
       nick TEXT NOT NULL,
       testo TEXT NOT NULL,
       autore TEXT NOT NULL,                  -- impronta (SHA-256) del codice segreto del browser di chi scrive
       rete TEXT NOT NULL DEFAULT '',         -- impronta dell'IP con la data: solo per i limiti anti-spam, cambia ogni giorno
       nascosto INTEGER NOT NULL DEFAULT 0    -- 1 = nascosto dalle segnalazioni o da chi amministra
     )`).run();
  await db.prepare(`CREATE INDEX IF NOT EXISTS messaggi_annuncio ON messaggi (annuncio, id)`).run();
  // una segnalazione per persona per messaggio
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS segnalazioni (
       messaggio INTEGER NOT NULL, chi TEXT NOT NULL, creato TEXT NOT NULL,
       PRIMARY KEY (messaggio, chi)
     )`).run();
  tabellaPronta = true;
}

export const SOGLIA_SEGNALAZIONI = 2;   // con 2 segnalazioni di persone diverse il messaggio si nasconde

export async function impronta(testo) {
  const h = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(String(testo)));
  return [...new Uint8Array(h)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

// invio email con Mailjet (stesse chiavi dei clandestini); risponde con l'esito in parole
export async function inviaEmail(env, oggetto, html, testo) {
  const mancano = ["MAILJET_API_KEY", "MAILJET_API_SECRET"].filter((k) => !env[k]);
  if (mancano.length) return `non inviata: manca ${mancano.join(" e ")}`;
  const dest = (env.EMAIL_DESTINATARI || "Giacomo.lo87@gmail.com").split(";").map((s) => s.trim()).filter(Boolean);
  try {
    const r = await fetch("https://api.mailjet.com/v3.1/send", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: "Basic " + btoa(`${env.MAILJET_API_KEY.trim()}:${env.MAILJET_API_SECRET.trim()}`) },
      body: JSON.stringify({ Messages: [{
        From: { Email: "siballanewsletter@gmail.com", Name: "siBalla" }, To: dest.map((Email) => ({ Email })),
        Subject: oggetto, TextPart: testo, HTMLPart: html,
      }] }),
    });
    const j = await r.json().catch(() => ({}));
    return r.ok && j.Messages?.[0]?.Status === "success" ? `inviata a ${dest.join(", ")}` : `non inviata (Mailjet HTTP ${r.status})`;
  } catch (e) {
    return "non inviata: " + e;
  }
}
