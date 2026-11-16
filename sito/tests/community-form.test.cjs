const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm');
function pagina(ok){
 const elementi={};
 for(const id of ['communityDialog','communityForm','inviaCommunity','cAntibot','cEsito','segnalaCommunity','chiudiCommunity','communityConferma']){
  elementi[id]={listeners:{},dataset:{},textContent:'',disabled:false,addEventListener(tipo,fn){this.listeners[tipo]=fn;},focus(){this.focused=true;}};
 }
 const dialog=elementi.communityDialog,form=elementi.communityForm;
 dialog.open=true;dialog.close=function(){this.open=false;this.listeners.close();};dialog.showModal=function(){this.open=true;};
 form.reportValidity=()=>true;form.reset=function(){this.resettato=true;};
 const anti={token:()=>'',inAttesa:()=>false,reset(){this.resettato=true;}};
 vm.runInNewContext(fs.readFileSync('sito/community.js','utf8'),{document:{getElementById:id=>elementi[id]},window:{siBallaAntibot:{monta:()=>anti}},FormData:class{*[Symbol.iterator](){yield ['titolo','Prova'];}},fetch:async()=>({ok,json:async()=>ok?{ok:true}:{errore:'Riprova piu tardi'}})});
 return{elementi,form,dialog,anti};
}
test('invio riuscito chiude la modale e conferma fuori dal modulo',async()=>{
 const {elementi,form,dialog,anti}=pagina(true);
 await form.listeners.submit({preventDefault(){}});
 assert.equal(dialog.open,false);assert.equal(form.resettato,true);assert.equal(elementi.segnalaCommunity.focused,true);
 assert.match(elementi.communityConferma.textContent,/richiesta.*ricevuta/);
 assert.equal(elementi.inviaCommunity.disabled,false);assert.equal(anti.resettato,true);
 elementi.segnalaCommunity.listeners.click();assert.equal(dialog.open,true);assert.equal(elementi.cEsito.textContent,'');
});
test('errore lascia la modale aperta e conserva i dati',async()=>{
 const {elementi,form,dialog}=pagina(false);
 await form.listeners.submit({preventDefault(){}});
 assert.equal(dialog.open,true);assert.equal(form.resettato,undefined);assert.equal(elementi.cEsito.dataset.stato,'errore');assert.equal(elementi.inviaCommunity.disabled,false);
});