const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('sito/functions/api/community.js', 'utf8').replace(/^import .*;\r?\n/gm, '').replace(/export /g, '');
const notifications = [];
const context = vm.createContext({ Response, URL, Date, console, pulisci:(s,n)=>s.trim().slice(0,n), esc:s=>s.replace(/</g,'&lt;'), antibotOk:async(env)=>!env.rejectBot, inviaEmail:async(...args)=>{ notifications.push(args); return 'inviata a admin'; }, prefissoProva:()=> '[PROVA] ' });
vm.runInContext(source, context);
const valid = { titolo:'Swing Genova', descrizione:'Community swing', link:'https://chat.whatsapp.com/esempio', regione:'Liguria', citta:'Genova', email:'proponente@example.com' };
function db(options={}) {
  const writes=[];
  return { writes, prepare(sql) { let args=[]; return { bind(...a){args=a; return this;}, async all(){return {results:options.legacy?[]:[{name:'citta'}]};}, async first(){return {n:options.limit?200:0};}, async run(){if(options.fail) throw Error('DB'); writes.push({sql,args}); return {meta:{last_row_id:42}};} }; } };
}
async function run(data, database=db(), extra={}) {
  const pending=[];
  const response=await context.onRequestPost({request:{json:async()=>data, headers:new Headers(),url:'https://anteprima.siballa.pages.dev/api/community'},env:{VISITE:database,...extra},waitUntil:p=>pending.push(p)});
  await Promise.all(pending);
  return { response, body:await response.json(), database };
}
test('salva i sei campi, notifica e restituisce solo id ed esito',async()=>{
  const before=notifications.length;
  const {response,body,database}=await run(valid);
  assert.equal(response.status,200); assert.deepEqual(body,{ok:true,id:42});
  const insert=database.writes.find(x=>x.sql.startsWith('INSERT'));
  assert.deepEqual(Array.from(insert.args.slice(1)),Object.values(valid));
  assert.equal(notifications.length,before+1);
  assert.match(notifications.at(-1)[3],/proponente@example.com/);
  assert.match(notifications.at(-1)[3],/Genova/);
  assert(database.writes.some(x=>x.sql.startsWith('UPDATE')&&x.args[0].startsWith('inviata')));
});
test('rifiuta campi mancanti, regione, email, link e payload non validi',async()=>{
  for(const data of [null,[],{},...Object.keys(valid).map(k=>({...valid,[k]:''})), {...valid,regione:'Atlantide'},{...valid,email:'a@'},{...valid,link:'javascript:alert(1)'},{...valid,titolo:'x'.repeat(121)}]){
    const {response,database}=await run(data); assert.equal(response.status,400); assert.equal(database.writes.length,0);
  }
});
test('limite, antispam e indisponibilità non danno successo',async()=>{
  assert.equal((await run(valid,db({limit:true}))).response.status,429);
  assert.equal((await run(valid,db(),{rejectBot:true})).response.status,400);
  assert.equal((await run(valid,null)).response.status,503);
  assert.equal((await run(valid,db({fail:true}))).response.status,500);
});
test('modale con sei campi obbligatori e script inclusi nel deploy',()=>{
 const html=fs.readFileSync('sito/community.html','utf8');
 for(const name of Object.keys(valid)) assert.match(html,new RegExp('name="'+name+'"[^>]*required'));
 assert.match(html,/<dialog[^>]*aria-labelledby=/);
 assert.match(html,/<script src="antibot.js"><\/script>\s*<script src="community.js">/);
 assert(!html.includes('mailto:'));
});
test('aggiorna la tabella esistente per conservare la citta', async()=>{
  const {response,database}=await run(valid,db({legacy:true}));
  assert.equal(response.status,200);
  assert(database.writes.some(x=>x.sql.startsWith('ALTER TABLE community_proposte ADD COLUMN citta')));
});
