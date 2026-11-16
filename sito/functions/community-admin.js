import { esc, uguali } from '../lib/clandestini.js';
import { preparaCommunity } from './api/community.js';

const headers = { 'Content-Type':'text/html; charset=utf-8', 'Cache-Control':'no-store', 'X-Robots-Tag':'noindex, nofollow', 'Referrer-Policy':'no-referrer' };
const nonTrovata = () => new Response('Not found', {status:404, headers:{'Cache-Control':'no-store'}});
const autorizzato = (env, chiave) => !!env.STATS_KEY && uguali(chiave, env.STATS_KEY);

export async function onRequestPost({request, env}) {
  const f = await request.formData(), chiave = String(f.get('chiave') || '');
  if (!autorizzato(env, chiave) || !env.VISITE) return nonTrovata();
  await preparaCommunity(env.VISITE);
  const azione = f.get('azione'), id = Number(f.get('id'));
  const stati = {approva:'ok', rifiuta:'no', togli:'no', riapri:'da valutare'};
  if (stati[azione] && Number.isSafeInteger(id) && id > 0) {
    await env.VISITE.prepare('UPDATE community_proposte SET stato = ?1 WHERE id = ?2').bind(stati[azione], id).run();
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
  return new Response(null, {status:303, headers:{Location:(f.get('ritorno') === '/statistiche' ? '/statistiche' : '/community-admin')+'?chiave='+encodeURIComponent(chiave)+(f.get('ritorno') === '/statistiche' ? '#community' : ''), 'Cache-Control':'no-store'}});
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
  return `    <h1>Gestione community</h1><nav><a href="/statistiche?chiave=${encodeURIComponent(chiave)}">Statistiche</a> &middot; <a href="/bacheca-admin?chiave=${encodeURIComponent(chiave)}">Bacheca</a> &middot; <a href="/clandestini-admin?chiave=${encodeURIComponent(chiave)}">Clandestini</a> &middot; <a href="/community">Pagina pubblica</a></nav>
    <p>Le proposte compaiono sul sito solo dopo l'approvazione. Togli dal sito le nasconde; Elimina definitivamente cancella anche la richiesta e il contatto privato. Gli aggiornamenti si vedono ricaricando la pagina pubblica.</p>
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
