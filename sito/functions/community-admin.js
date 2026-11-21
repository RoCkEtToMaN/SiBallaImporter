import { esc, uguali, pulisci } from '../lib/clandestini.js';
import { preparaCommunity, REGIONI } from './api/community.js';
import { inviaEmail, prefissoProva } from '../lib/bacheca.js';

const headers = { 'Content-Type':'text/html; charset=utf-8', 'Cache-Control':'no-store', 'X-Robots-Tag':'noindex, nofollow', 'Referrer-Policy':'no-referrer' };
const nonTrovata = () => new Response('Not found', {status:404, headers:{'Cache-Control':'no-store'}});
const autorizzato = (env, chiave) => !!env.STATS_KEY && uguali(chiave, env.STATS_KEY);

// email a chi ha proposto la community, quando viene approvata (una volta sola)
export async function avvisaApprovazione(env, origine, r) {
  const link = `${origine}/community`, oggetto = 'La tua community è su siBalla';
  const righe = [`La community "${r.titolo}" (${r.citta}, ${r.regione}) è stata approvata ed è ora visibile su siBalla.`,
    'Grazie per aver contribuito a far crescere la scena swing!'];
  const html = `<div style="font-family:system-ui,Segoe UI,Roboto,sans-serif;font-size:15px;color:#111">
    <h2 style="margin:0 0 12px">${esc(oggetto)}</h2>${righe.map(x => `<p style="margin:0 0 8px">${esc(x)}</p>`).join('')}
    <p style="margin:18px 0"><a href="${esc(link)}" style="background:#FFD400;color:#0E0E12;font-weight:700;text-decoration:none;padding:10px 18px;border-radius:10px;display:inline-block">Vedi le community</a></p>
    <p style="margin:0;color:#888;font-size:12px">Ricevi questa email perché hai segnalato una community su siBalla. Il tuo indirizzo non è pubblicato.</p></div>`;
  const testo = `${oggetto}\n\n${righe.join('\n')}\n\nVedi le community: ${link}\n\n` +
    'Ricevi questa email perché hai segnalato una community su siBalla. Il tuo indirizzo non è pubblicato.';
  return inviaEmail(env, prefissoProva(origine) + oggetto, html, testo, r.email);
}

export async function onRequestPost({request, env}) {
  const f = await request.formData(), chiave = String(f.get('chiave') || '');
  if (!autorizzato(env, chiave) || !env.VISITE) return nonTrovata();
  await preparaCommunity(env.VISITE);
  const azione = f.get('azione'), id = Number(f.get('id'));
  const stati = {approva:'ok', rifiuta:'no', togli:'no', riapri:'da valutare'};
  if (azione === 'aggiungi') {
    const limiti = {titolo:120, descrizione:1500, link:500, regione:80, citta:120, email:254};
    const dati = {};
    for (const [campo, limite] of Object.entries(limiti)) {
      const valore = String(f.get(campo) || '').trim();
      if (!valore || valore.length > limite) return new Response('Compila tutti i campi. Torna indietro per correggere.', {status:400, headers});
      dati[campo] = valore;
    }
    let linkValido = false;
    try {
      const u = new URL(dati.link);
      linkValido = ['https:', 'http:'].includes(u.protocol) && u.hostname.includes('.') && !u.username && !u.password;
    } catch {}
    const titolo = pulisci(dati.titolo,120), descrizione = pulisci(dati.descrizione,1500), citta = pulisci(dati.citta,120);
    if (!titolo || !descrizione || !citta || !REGIONI.includes(dati.regione) || !linkValido || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(dati.email))
      return new Response('Controlla titolo, descrizione, regione, citta, link ed email. Torna indietro per correggere.', {status:400, headers});
    await env.VISITE.prepare(`INSERT INTO community_proposte (creato, titolo, descrizione, link, regione, citta, email, stato, notifica)
      VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, 'ok', 'Inserita dall amministratore')`)
      .bind(new Date().toISOString(), titolo, descrizione, dati.link, dati.regione, citta, dati.email).run();
  } else if (stati[azione] && Number.isSafeInteger(id) && id > 0) {
    const prima = azione === 'approva' ? await env.VISITE.prepare('SELECT * FROM community_proposte WHERE id = ?1').bind(id).first() : null;
    await env.VISITE.prepare('UPDATE community_proposte SET stato = ?1 WHERE id = ?2').bind(stati[azione], id).run();
    // appena approvata una proposta arrivata dal sito: avviso a chi l'ha segnalata (una volta sola)
    if (prima && prima.stato === 'da valutare' && !/approvazione: inviata/.test(prima.notifica || '')) {
      try {
        const esito = await avvisaApprovazione(env, new URL(request.url).origin, prima);
        await env.VISITE.prepare('UPDATE community_proposte SET notifica = ?1 WHERE id = ?2')
          .bind(`${prima.notifica || ''} · approvazione: ${esito}`.slice(0, 500), id).run();
      } catch (e) { console.log('avviso approvazione community non inviato: ' + e); }
    }
  } else if (azione === 'elimina' && Number.isSafeInteger(id) && id > 0) {
    await env.VISITE.prepare('DELETE FROM community_proposte WHERE id = ?1').bind(id).run();
  } else if (azione === 'nascondi') {
    const link = String(f.get('link') || '').trim(), nome = String(f.get('nome') || '').slice(0,120);
    let valido = false;
    try { valido = ['http:', 'https:'].includes(new URL(link).protocol) && link.length <= 500; } catch {}
    if (!valido) return new Response('Link non valido', {status:400});
    await env.VISITE.prepare('INSERT OR REPLACE INTO community_escluse (link, nome, creato) VALUES (?1, ?2, ?3)')
      .bind(link, nome, new Date().toISOString()).run();
  } else if (azione === 'ripristina') {
    await env.VISITE.prepare('DELETE FROM community_escluse WHERE link = ?1').bind(String(f.get('link') || '')).run();
  } else return new Response('Azione non valida', {status:400});
  const ritorno = ['/statistiche', '/bacheca-admin'].includes(f.get('ritorno')) ? f.get('ritorno') : '/community-admin';
  return new Response(null, {status:303, headers:{Location:ritorno+'?chiave='+encodeURIComponent(chiave)+(ritorno !== '/community-admin' ? '#community' : ''), 'Cache-Control':'no-store'}});
}

export async function onRequestGet({request, env}) {
  const chiave = new URL(request.url).searchParams.get('chiave') || '';
  if (!autorizzato(env, chiave)) return nonTrovata();
  if (!env.VISITE) return new Response('Database non collegato: manca VISITE.', {status:503,headers});
  return new Response(`<!doctype html><html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow"><title>Gestione community siBalla</title>
    <style>body{background:#0E0E12;color:#fff;font:16px/1.5 system-ui;margin:0}main{max-width:800px;padding:24px 16px;margin:auto}a,h2{color:#FFD400}article{background:#1B1B22;border-radius:14px;padding:16px;margin:12px 0;overflow-wrap:anywhere}h3,p{margin:4px 0 10px}.meta{font-size:13px;color:#aaa}.descrizione{white-space:pre-wrap}.azioni{display:flex;flex-wrap:wrap;gap:8px}form{margin:0}button{font:inherit;padding:8px 12px;border:1px solid #555;border-radius:8px;background:#26262F;color:#fff;cursor:pointer}section{margin-top:28px}</style></head><body><main>
${await pannelloCommunity(env.VISITE, chiave)}</main></body></html>`, {headers});
}
export async function pannelloCommunity(db, chiave, ritorno = '/community-admin') {
  await preparaCommunity(db);
  const {results: richieste} = await db.prepare('SELECT * FROM community_proposte ORDER BY creato DESC').all();
  const {results: escluse} = await db.prepare('SELECT * FROM community_escluse ORDER BY nome').all();
  const chiaveInput = `<input type="hidden" name="ritorno" value="${esc(ritorno)}"><input type="hidden" name="chiave" value="${esc(chiave)}">`;
  const pulsante = (r, azione, testo) => `<form method="post" action="/community-admin"${azione === 'elimina' ? ' onsubmit="return confirm(\'Eliminare definitivamente questa richiesta e il contatto?\')"' : ''}>${chiaveInput}<input type="hidden" name="id" value="${r.id}"><button name="azione" value="${azione}">${testo}</button></form>`;
  const scheda = (r) => `<article><h3>${esc(r.titolo)}</h3><p>${esc(r.regione)} &rarr; ${esc(r.citta)}</p>
    <p class="descrizione">${esc(r.descrizione)}</p><p>Link: ${esc(r.link)}</p><p>Email privata: ${esc(r.email)}</p>
    <p class="meta">Richiesta #${r.id} del ${esc(new Date(r.creato).toLocaleString('it-IT',{timeZone:'Europe/Rome'}))}<br>Notifica: ${esc(r.notifica)}</p>
    <div class="azioni">${r.stato === 'ok' ? pulsante(r,'togli','Togli dal sito') : pulsante(r,'approva','Approva e pubblica')}
    ${r.stato === 'da valutare' ? pulsante(r,'rifiuta','Rifiuta') : pulsante(r,'riapri','Rimetti in attesa')}${pulsante(r,'elimina','Elimina definitivamente')}</div></article>`;
  const gruppo = (titolo, stato) => { const righe = richieste.filter(r=>r.stato===stato); return `<section><h2>${titolo} (${righe.length})</h2>${righe.map(scheda).join('') || '<p>Nessuna community.</p>'}</section>`; };
  return `    <style>.community-aggiungi label{display:block;margin:10px 0}.community-aggiungi input,.community-aggiungi textarea,.community-aggiungi select{display:block;box-sizing:border-box;width:100%;font:inherit;background:#0E0E12;color:#fff;border:1px solid #555;border-radius:8px;padding:9px;margin-top:5px}.community-aggiungi button{margin-top:10px}</style><h1>Gestione community WhatsApp</h1><nav><a href="/statistiche?chiave=${encodeURIComponent(chiave)}">Statistiche</a> &middot; <a href="/bacheca-admin?chiave=${encodeURIComponent(chiave)}">Bacheca</a> &middot; <a href="/clandestini-admin?chiave=${encodeURIComponent(chiave)}">Clandestini</a> &middot; <a href="/community">Pagina pubblica</a></nav>
    <p>Le proposte compaiono sul sito solo dopo l'approvazione; approvandole, chi le ha segnalate riceve un'email. Togli dal sito le nasconde; Elimina definitivamente cancella anche la richiesta e il contatto privato. Gli aggiornamenti si vedono ricaricando la pagina pubblica.</p>
    <section class="community-aggiungi"><h2>Aggiungi una community WhatsApp</h2><p>La community inserita da qui viene pubblicata direttamente. L'email di contatto resta privata.</p>
    <form method="post" action="/community-admin">${chiaveInput}<input type="hidden" name="azione" value="aggiungi">
    <label>Titolo community<input name="titolo" required maxlength="120"></label>
    <label>Descrizione<textarea name="descrizione" required maxlength="1500" rows="3"></textarea></label>
    <label>Link della community<input name="link" type="url" required maxlength="500" placeholder="https://"></label>
    <label>Regione<select name="regione" required><option value="">Scegli una regione</option>${REGIONI.map(r=>`<option>${esc(r)}</option>`).join('')}</select></label>
    <label>Citt&agrave;<input name="citta" required maxlength="120" autocomplete="address-level2"></label>
    <label>Email di contatto (privata)<input name="email" type="email" required maxlength="254" autocomplete="email"></label>
    <button>Aggiungi e pubblica</button></form></section>
    ${gruppo('Da valutare','da valutare')}${gruppo('Approvate','ok')}${gruppo('Rifiutate o tolte','no')}
    <section><h2>Community dall'elenco iniziale</h2><p>Puoi toglierle dal sito e ripristinarle qui. Il file di origine rimane conservato.</p><div id="iniziali">Caricamento...</div></section>
    <section><h2>Tolte dall'elenco iniziale</h2>${escluse.map(r=>`<article><h3>${esc(r.nome)}</h3><p>${esc(r.link)}</p><form method="post" action="/community-admin">${chiaveInput}<input type="hidden" name="link" value="${esc(r.link)}"><button name="azione" value="ripristina">Ripristina sul sito</button></form></article>`).join('') || '<p>Nessuna.</p>'}</section>
    <template id="nascondi"><form method="post" action="/community-admin">${chiaveInput}<input type="hidden" name="link"><input type="hidden" name="nome"><button name="azione" value="nascondi">Togli dal sito</button></form></template>
    <script>
    (function(){
      var esclusi = new Set(${JSON.stringify(escluse.map(r=>r.link)).replace(/</g,'\\u003c')});
      fetch('/community.json',{cache:'no-store'}).then(r=>{if(!r.ok)throw Error();return r.json();}).then(d=>{
        var box=document.getElementById('iniziali');box.textContent='';
        (d.righe||[]).filter(r=>!esclusi.has(r.link)).forEach(r=>{
          var card=document.createElement('article'),titolo=document.createElement('h3'),luogo=document.createElement('p');
          titolo.textContent=r.nome;luogo.textContent=[r.regione,r.citta].filter(Boolean).join(' / ');card.append(titolo,luogo);
          var form=document.getElementById('nascondi').content.firstElementChild.cloneNode(true);
          form.elements.link.value=r.link;form.elements.nome.value=r.nome;
          form.addEventListener('submit',e=>{if(!confirm('Togliere questa community dal sito? Potrai ripristinarla da qui.'))e.preventDefault();});
          card.append(form);box.append(card);
        });if(!box.children.length)box.textContent='Nessuna community.';
      }).catch(()=>document.getElementById('iniziali').textContent='Impossibile leggere l elenco iniziale. Ricarica la pagina.');
    })();
    </script>`;
}
