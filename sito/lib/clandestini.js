// Clandestini: eventi proposti dagli utenti dal sito, salvati nello stesso database D1 delle visite (binding "VISITE").
// stato: "attesa" (appena proposto, non visibile) -> "ok" (approvato, visibile subito sul sito) oppure "no" (rifiutato).
// Si approvano dalla pagina nascosta /clandestini-admin?chiave=... (stessa chiave STATS_KEY delle statistiche).

let tabellaPronta = false;

export async function preparaClandestini(db) {
  if (tabellaPronta) return;
  await db.prepare(
    `CREATE TABLE IF NOT EXISTS clandestini (
       id INTEGER PRIMARY KEY AUTOINCREMENT,
       creato TEXT NOT NULL,          -- ISO, ora UTC
       stato TEXT NOT NULL DEFAULT 'attesa',
       nome TEXT NOT NULL,
       giorno TEXT NOT NULL,          -- yyyy-mm-dd
       ora TEXT NOT NULL DEFAULT '',  -- hh:mm
       citta TEXT NOT NULL,
       luogo TEXT NOT NULL,
       lat REAL, lng REAL,
       stili TEXT NOT NULL DEFAULT '',
       gratis INTEGER NOT NULL DEFAULT 1,
       link TEXT NOT NULL DEFAULT '',
       dettagli TEXT NOT NULL DEFAULT '',
       contatto TEXT NOT NULL DEFAULT ''   -- solo per chi approva, mai mostrato sul sito
     )`).run();
  tabellaPronta = true;
}

export function pulisci(s, max) {
  return String(s ?? "").replace(/[\x00-\x09\x0b-\x1f<>]/g, "").replace(/[ \t]+/g, " ").trim().slice(0, max);
}

export function esc(s) {
  return String(s ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

export function uguali(a, b) { // confronto a tempo costante
  if (a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

// "yyyy-mm-dd" -> "dd/mm/yyyy" (formato delle date dello Sheet, che il sito sa già leggere)
export function dataSheet(g) {
  const [y, m, d] = g.split("-");
  return `${d}/${m}/${y}`;
}
