const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm');
const esc=s=>String(s??'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
const source=fs.readFileSync('sito/functions/community-admin.js','utf8').replace(/^import .*;\r?\n/gm,'').replace(/export async function/g,'async function');
const context=vm.createContext({Response,URL,Date,console,esc,uguali:(a,b)=>a===b,preparaCommunity:async()=>{}});
vm.runInContext(source,context);
function db(){const writes=[];return{writes,prepare(sql){let args=[];return{bind(...a){args=a;return this;},async run(){writes.push({sql,args});},async all(){return{results:sql.includes('community_escluse')?[]:[{id:7,titolo:'<script>bad</script>',regione:'Liguria',citta:'Genova',descrizione:'Swing',link:'https://example.com',email:'private@example.com',creato:'2026-11-16T12:00:00Z',notifica:'inviata',stato:'da valutare'}]};}};}};}
async function post(fields,key='secret'){
 const database=db(),f=new FormData();Object.entries({chiave:key,id:'7',...fields}).forEach(([k,v])=>f.set(k,v));
 const response=await context.onRequestPost({request:{formData:async()=>f},env:{STATS_KEY:'secret',VISITE:database}});
 return{response,database};
}
test('pagina e azioni protette dalla chiave',async()=>{
 for(const key of ['', 'wrong']){
  assert.equal((await context.onRequestGet({request:{url:'https://example.com/community-admin?chiave='+key},env:{STATS_KEY:'secret',VISITE:db()}})).status,404);
  const result=await post({azione:'elimina'},key);assert.equal(result.response.status,404);assert.equal(result.database.writes.length,0);
 }
});
test('approva, toglie ed elimina la richiesta corretta',async()=>{
 for(const [azione,stato] of [['approva','ok'],['togli','no'],['rifiuta','no'],['riapri','da valutare']]){
  const {response,database}=await post({azione});assert.equal(response.status,303);assert.deepEqual(database.writes[0].args,[stato,7]);
 }
 const {response,database}=await post({azione:'elimina'});assert.equal(response.status,303);assert.match(database.writes[0].sql,/^DELETE FROM community_proposte/);assert.deepEqual(database.writes[0].args,[7]);
});
test('rimozione e ripristino delle community iniziali',async()=>{
 const hidden=await post({azione:'nascondi',link:'https://example.com/group',nome:'Swing'});assert.equal(hidden.response.status,303);assert.match(hidden.database.writes[0].sql,/INSERT OR REPLACE INTO community_escluse/);
 const restored=await post({azione:'ripristina',link:'https://example.com/group'});assert.match(restored.database.writes[0].sql,/DELETE FROM community_escluse/);
 assert.equal((await post({azione:'nascondi',link:'javascript:alert(1)'})).response.status,400);
});
test('pagina privata mostra esito notifica ed esegue escaping',async()=>{
 const r=await context.onRequestGet({request:{url:'https://example.com/community-admin?chiave=secret'},env:{STATS_KEY:'secret',VISITE:db()}});
 const html=await r.text();assert.equal(r.headers.get('Cache-Control'),'no-store');assert.equal(r.headers.get('Referrer-Policy'),'no-referrer');assert.match(html,/&lt;script>/);assert(!html.includes('<script>bad'));
 assert.match(html,/Notifica: inviata/);assert.match(html,/Elimina definitivamente/);
});
test('API pubblica seleziona solo dati pubblici approvati',async()=>{
 const api=fs.readFileSync('sito/functions/api/community.js','utf8').replace(/^import .*;\r?\n/gm,'').replace(/export async function/g,'async function');
 const c=vm.createContext({Response,console});vm.runInContext(api,c);
 const queries=[];
 const database={prepare(sql){queries.push(sql);return{async run(){},async all(){return{results:sql.includes('PRAGMA')?[{name:'citta'}]:sql.includes('SELECT link')?[{link:'https://hidden.example'}]:[{nome:'Swing',regione:'Liguria',citta:'Genova'}]};}};}};
 const r=await c.onRequestGet({env:{VISITE:database}});const body=await r.json();
 const query=queries.find(q=>q.includes('titolo AS nome'));assert.match(query,/WHERE stato = 'ok'/);assert(!query.includes('email'));assert(!query.includes('notifica'));
 assert.deepEqual(body.esclusi,['https://hidden.example']);assert(!JSON.stringify(body).includes('private@example.com'));
});
test('il pannello riutilizzato dalle statistiche mostra richieste e gruppi iniziali',async()=>{
 const html=await context.pannelloCommunity(db(),'secret');
 assert.match(html,/Community dall.elenco iniziale/);
 assert.match(html,/Da valutare/);
 assert.match(html,/Elimina definitivamente/);
 assert.match(html,/Togli dal sito/);
 const stats=fs.readFileSync('sito/functions/statistiche.js','utf8');
 assert.match(stats,/await pannelloCommunity\(env.VISITE, chiave, '\/statistiche'\)/);
 assert.match(stats,/Community presenti e richieste/);
});

test('azioni avviate dalle statistiche tornano alla sezione community',async()=>{
 const {response}=await post({azione:'elimina',ritorno:'/statistiche'});
 assert.equal(response.headers.get('Location'),'/statistiche?chiave=secret#community');
 const html=await context.pannelloCommunity(db(),'secret','/statistiche');
 assert.match(html,/<form method="post" action="\/community-admin"/);
 assert.match(html,/name="ritorno" value="\/statistiche"/);
});
