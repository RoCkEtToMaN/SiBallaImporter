// Sondaggio "Quali rubriche vorresti?" nella pagina /rubriche.
// Un voto per browser (riconosciuto dall'impronta del suo codice segreto, come nella bacheca): votando di nuovo
// si cambia il proprio voto. Si possono scegliere più rubriche e proporne una nuova (testo libero, max 200).
// I risultati si vedono nella pagina dopo il voto e in /statistiche (lì anche le proposte).

// Per aggiungere o togliere un'opzione basta cambiare questa lista (la chiave non va cambiata dopo i primi voti).
export const OPZIONI = [
  ["mossa", "La mossa della settimana", "un passo spiegato in un video breve"],
  ["quiz", "Quiz swing", "musica, storia, passi: quanto ne sai?"],
  ["giornate", "Giornate speciali", "compleanni dei grandi ballerini e dei musicisti"],
  ["wrapped", "Il tuo anno di swing", "a dicembre: serate, città, eventi dell'anno"],
  ["storie", "Storie e interviste", "insegnanti, DJ e musicisti della scena"],
  ["musica", "Brani da conoscere", "un pezzo alla settimana e perché si balla bene"],
  ["vestire", "Cosa mi metto", "scarpe e vestiti per ballare"],
];
const CHIAVI = new Set(OPZIONI.map((o) => o[0]));

let pronta = false;
export async function preparaSondaggio(db) {
  if (pronta) return;
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS voti_rubriche (
       chi TEXT PRIMARY KEY,                  -- impronta (SHA-256) del codice segreto del browser
       scelte TEXT NOT NULL DEFAULT '',       -- chiavi separate da virgola
       proposta TEXT NOT NULL DEFAULT '',     -- rubrica proposta (testo libero)
       creato TEXT NOT NULL, aggiornato TEXT NOT NULL,
       rete TEXT NOT NULL DEFAULT ''          -- impronta dell'IP con la data: solo per il limite anti-spam
     )`).run();
  pronta = true;
}

export function pulisciScelte(a) {
  return [...new Set((Array.isArray(a) ? a : []).map(String).filter((k) => CHIAVI.has(k)))];
}

// { totale, conta: { chiave: n }, proposte: [{ proposta, aggiornato }] }
export async function risultati(db, conProposte = false) {
  await preparaSondaggio(db);
  const { results } = await db.prepare(`SELECT scelte FROM voti_rubriche`).all();
  const conta = Object.fromEntries(OPZIONI.map((o) => [o[0], 0]));
  for (const r of results) for (const k of r.scelte.split(",")) if (k in conta) conta[k]++;
  const out = { totale: results.length, conta };
  if (conProposte) out.proposte = (await db.prepare(
    `SELECT proposta, aggiornato FROM voti_rubriche WHERE proposta <> '' ORDER BY aggiornato DESC LIMIT 100`).all()).results;
  return out;
}
