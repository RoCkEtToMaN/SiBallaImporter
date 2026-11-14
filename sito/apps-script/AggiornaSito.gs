/**
 * SiBalla - aggiorna il sito pubblico quando cambia lo Sheet.
 *
 * Da incollare in Estensioni > Apps Script dello Sheet degli eventi.
 * - Automatico: a ogni modifica aspetta ATTESA_MINUTI senza altre modifiche, poi avvia il workflow
 *   "Sito pubblico" su GitHub (così una sessione di modifiche fa un solo aggiornamento).
 * - Manuale: menu SiBalla > Aggiorna sito ora.
 * - Menu SiBalla > Aggiorna versione di prova: pubblica il ramo "anteprima" (https://anteprima.siballa.pages.dev),
 *   dove si vedono le novità non ancora pubbliche (es. le Rubriche della scheda "Contenuti").
 *   Anche l'aggiornamento automatico, se RAMO_PROVA non è vuoto, aggiorna pure la versione di prova.
 * - Menu SiBalla > Carica immagini e aggiorna sito: giro veloce dell'importatore ("solo immagini": carica
 *   su Storage le immagini di data/immagini, dei siti e i loghi, scrive "sì" nella colonna Immagine e
 *   aggiorna il DB dell'app), poi aggiorna il sito. Ci mette 5-10 minuti.
 * - Menu SiBalla > Eventi passati: controllo con Gemini delle righe "storico" (workflow "Riclassifica storico"):
 *   "anteprima" manda l'esito per email (elenco dei non swing); "controlla e scrivi" mette la nota (swing) oppure "no" + motivo.
 *   Le righe già controllate hanno la nota e vengono saltate. Ci mette 5-15 minuti.
 *
 * Configurazione (una volta sola):
 * 1. Impostazioni progetto (rotella) > Proprietà script > aggiungi GITHUB_TOKEN = token GitHub con permesso
 *    "Actions: Read and write" sul repo (vedi il documento "Configurazione sito pubblico").
 * 2. Nell'editor scegli la funzione "installa" ed esegui: crea il trigger e chiede le autorizzazioni.
 *
 * Le modifiche scritte dall'importatore tramite API non avviano lo script: per quelle c'è il giro orario.
 */

const REPO = 'RoCkEtToMaN/SiBallaImporter';
const WORKFLOW = 'sito.yml';
const WORKFLOW_IMPORTATORE = 'importatore.yml';
const WORKFLOW_STORICO = 'riclassifica-storico.yml';
const BRANCH = 'master';
const RAMO_PROVA = 'anteprima';   // versione di prova; '' per non aggiornarla mai dallo Sheet
const ATTESA_MINUTI = 3;

/** Menu nello Sheet (si crea a ogni apertura). */
function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('SiBalla')
    .addItem('Aggiorna sito ora', 'aggiornaSitoOra')
    .addItem('Carica immagini e aggiorna sito', 'caricaImmagini')
    .addSeparator()
    .addItem('Aggiorna versione di prova', 'aggiornaProva')
    .addSeparator()
    .addSubMenu(SpreadsheetApp.getUi().createMenu('Eventi passati (storico)')
      .addItem('Controlla con Gemini: solo anteprima', 'storicoAnteprima')
      .addItem('Controlla con Gemini e scrivi nello Sheet', 'storicoScrivi'))
    .addToUi();
}

/** Voce di menu: Gemini controlla le righe "storico" e scrive l'esito solo nel log del workflow su GitHub. */
function storicoAnteprima() {
  const esito = avviaWorkflow_(WORKFLOW_STORICO, { scrivi: 'false' });
  SpreadsheetApp.getActive().toast(
    esito.ok
      ? 'Anteprima avviata: tra 5-15 minuti ti arriva l\'esito per email (elenco dei non swing, log in allegato). Lo Sheet non cambia.'
      : 'Errore: ' + esito.messaggio,
    'SiBalla', 15);
}

/** Voce di menu: come l'anteprima, ma scrive nello Sheet (swing = nota, non swing = "no" con il motivo). */
function storicoScrivi() {
  const ui = SpreadsheetApp.getUi();
  const conferma = ui.alert('Eventi passati',
    'Gemini controlla le righe "storico" ancora senza nota.\n' +
    'Swing: resta "storico" con la nota "Gemini: swing".\nNon swing: diventa "no" con il motivo nella nota.\n\nProcedo?',
    ui.ButtonSet.OK_CANCEL);
  if (conferma !== ui.Button.OK) return;
  const esito = avviaWorkflow_(WORKFLOW_STORICO, { scrivi: 'true' });
  SpreadsheetApp.getActive().toast(
    esito.ok
      ? 'Controllo avviato: tra 5-15 minuti trovi le note nella colonna "Luogo verificato" e ti arriva il riepilogo per email.'
      : 'Errore: ' + esito.messaggio,
    'SiBalla', 15);
}

/** Voce di menu: giro veloce dell'importatore (solo immagini e DB); a fine giro parte anche il sito. */
function caricaImmagini() {
  const esito = avviaWorkflow_(WORKFLOW_IMPORTATORE, { solo_immagini: 'true' });
  SpreadsheetApp.getActive().toast(
    esito.ok
      ? 'Caricamento avviato: tra 5-10 minuti la colonna Immagine e il sito saranno aggiornati.'
      : 'Errore: ' + esito.messaggio,
    'SiBalla', 10);
}

/** Voce di menu: avvia subito l'aggiornamento. */
function aggiornaSitoOra() {
  const esito = avviaWorkflow_();
  SpreadsheetApp.getActive().toast(
    esito.ok ? 'Aggiornamento avviato: il sito sarà aggiornato in 1-2 minuti.' : 'Errore: ' + esito.messaggio,
    'SiBalla', 10);
}

/** Voce di menu: aggiorna la versione di prova (ramo RAMO_PROVA) con i dati attuali dello Sheet. */
function aggiornaProva() {
  const esito = avviaWorkflow_(WORKFLOW, null, RAMO_PROVA);
  SpreadsheetApp.getActive().toast(
    esito.ok ? 'Versione di prova in aggiornamento: pronta in 1-2 minuti su anteprima.siballa.pages.dev.' : 'Errore: ' + esito.messaggio,
    'SiBalla', 10);
}

/** Da eseguire una volta dall'editor: crea il trigger "alla modifica" (eliminando eventuali doppioni). */
function installa() {
  if (!PropertiesService.getScriptProperties().getProperty('GITHUB_TOKEN')) {
    throw new Error('Manca la proprietà script GITHUB_TOKEN (Impostazioni progetto > Proprietà script).');
  }
  ScriptApp.getProjectTriggers()
    .filter(t => ['allaModifica', 'pubblicaSeFermo'].includes(t.getHandlerFunction()))
    .forEach(t => ScriptApp.deleteTrigger(t));
  ScriptApp.newTrigger('allaModifica').forSpreadsheet(SpreadsheetApp.getActive()).onChange().create();
  Logger.log('Trigger installato: il sito si aggiornerà %s minuti dopo l\'ultima modifica.', ATTESA_MINUTI);
}

/** Trigger "alla modifica": segna l'ora e programma un controllo, se non ce n'è già uno. */
function allaModifica() {
  const lock = LockService.getScriptLock();
  if (!lock.tryLock(10000)) return;
  try {
    PropertiesService.getScriptProperties().setProperty('ULTIMA_MODIFICA', String(Date.now()));
    if (!controlloInAttesa_()) programmaControllo_(ATTESA_MINUTI * 60 * 1000);
  } finally {
    lock.releaseLock();
  }
}

/** Trigger a tempo: se dall'ultima modifica è passato abbastanza tempo pubblica, altrimenti aspetta ancora. */
function pubblicaSeFermo() {
  const lock = LockService.getScriptLock();
  lock.waitLock(30000);
  try {
    ScriptApp.getProjectTriggers()
      .filter(t => t.getHandlerFunction() === 'pubblicaSeFermo')
      .forEach(t => ScriptApp.deleteTrigger(t));
    const ultima = Number(PropertiesService.getScriptProperties().getProperty('ULTIMA_MODIFICA') || 0);
    const mancano = ATTESA_MINUTI * 60 * 1000 - (Date.now() - ultima);
    if (mancano > 15000) {
      programmaControllo_(mancano);
      return;
    }
  } finally {
    lock.releaseLock();
  }
  const esito = avviaWorkflow_();
  // anche la versione di prova, così le novità (es. Rubriche) seguono lo Sheet; un suo errore non blocca il sito pubblico
  if (RAMO_PROVA) avviaWorkflow_(WORKFLOW, null, RAMO_PROVA);
  if (!esito.ok) throw new Error(esito.messaggio); // compare in Esecuzioni e Google manda un'email di errore
}

function controlloInAttesa_() {
  return ScriptApp.getProjectTriggers().some(t => t.getHandlerFunction() === 'pubblicaSeFermo');
}

function programmaControllo_(ms) {
  ScriptApp.newTrigger('pubblicaSeFermo').timeBased().after(Math.max(ms, 60000)).create();
}

/** Chiede a GitHub di avviare un workflow (di default quello del sito, sul ramo BRANCH), con gli input indicati. */
function avviaWorkflow_(workflow, inputs, ramo) {
  workflow = workflow || WORKFLOW;
  ramo = ramo || BRANCH;
  const token = PropertiesService.getScriptProperties().getProperty('GITHUB_TOKEN');
  if (!token) return { ok: false, messaggio: 'manca la proprietà script GITHUB_TOKEN' };
  const risposta = UrlFetchApp.fetch(
    'https://api.github.com/repos/' + REPO + '/actions/workflows/' + workflow + '/dispatches', {
      method: 'post',
      contentType: 'application/json',
      headers: {
        Authorization: 'Bearer ' + token,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
      payload: JSON.stringify(inputs ? { ref: ramo, inputs: inputs } : { ref: ramo }),
      muteHttpExceptions: true,
    });
  const codice = risposta.getResponseCode();
  if (codice === 204) return { ok: true };
  const spiegazioni = {
    401: 'token non valido o scaduto',
    403: 'il token non ha il permesso Actions: Read and write su questo repo',
    404: 'repo o workflow non trovati, oppure il token non vede il repo',
  };
  return { ok: false, messaggio: 'GitHub ' + codice + ' (' + (spiegazioni[codice] || risposta.getContentText()) + ')' };
}
