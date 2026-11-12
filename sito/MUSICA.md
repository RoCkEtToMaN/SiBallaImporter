# Musica: template dello Google Sheet

La pagina `/musica` della versione di prova usa la scheda **Musica** dello stesso Google Sheet degli eventi.

- **Riga 1:** nota libera (non viene pubblicata).
- **Riga 2:** intestazioni esatte della tabella qui sotto.
- **Dalla riga 3:** selezioni e brani. Una riga viene pubblicata solo con **Attivo = si** (va bene anche sì).

| Colonna | Intestazione | Riga `selezione` | Riga `brano` |
|---|---|---|---|
| A | Tipo | `selezione` | `brano` |
| B | Selezione | Codice univoco, es. `calma` | Codice della selezione a cui appartiene, es. `calma` |
| C | Titolo selezione | Titolo del riquadro e del filtro, es. Con calma | Lascia vuoto |
| D | Sottotitolo | Testo breve sopra il titolo | Lascia vuoto |
| E | Descrizione | Descrizione del riquadro | Lascia vuoto |
| F | Titolo brano | Lascia vuoto | Titolo del brano o della playlist |
| G | Artista | Lascia vuoto | Nome artista o curatore |
| H | Link YouTube | Lascia vuoto | Link HTTPS del video o della playlist YouTube |
| I | Ordine | Posizione del riquadro | Posizione del brano nel suo riquadro |
| J | Attivo | `si` per mostrare; `no` o vuoto per nascondere tutta la selezione | `si` per mostrare; `no` o vuoto per nascondere il brano |

## Esempio minimo

| Tipo | Selezione | Titolo selezione | Sottotitolo | Descrizione | Titolo brano | Artista | Link YouTube | Ordine | Attivo |
|---|---|---|---|---|---|---|---|---|---|
| selezione | calma | Con calma | Respira e prova | Musica per esercitarsi. | | | | 1 | si |
| brano | calma | | | | Easy Does It | Count Basie | https://www.youtube.com/watch?v=lyRlLaQiguQ | 1 | si |

Per aggiungere un brano, copia una riga `brano` e cambia selezione, titolo, artista, link e ordine. Per aggiungere una selezione, crea una riga `selezione` con un nuovo codice e usa lo stesso codice nelle sue righe `brano`. Ogni codice deve avere una sola riga `selezione` attiva. Puoi spostare fisicamente le righe: conta la colonna Ordine; a parità rimane l'ordine del foglio.

Per cambiare il titolo di un riquadro modifica **Titolo selezione**, lasciando invariato il codice **Selezione**. Per nascondere un intero riquadro metti `no` sulla sua riga `selezione`: i suoi brani non compaiono, anche se attivi. Una selezione attiva senza brani mostra “Nuovi brani in arrivo”.

Sono supportati link `youtube.com/watch?v=...`, `youtu.be/...`, Shorts e playlist `youtube.com/playlist?list=...`. Le ricerche `youtube.com/results?search_query=...` si aprono su YouTube. Il lettore si carica solo dopo la scelta; non parte automaticamente. Spotify non è previsto da questo template.

## Aggiornamento

Dopo le modifiche usa **SiBalla → Aggiorna versione di prova**, oppure attendi l'aggiornamento automatico già configurato. La pagina aggiornata si trova su https://anteprima.siballa.pages.dev/musica.

Le intestazioni devono rimanere alla riga 2; non rinominare la scheda Musica. Le colonne possono essere riordinate perché l'esportazione le identifica dal nome.

## File del progetto

`elenchi/musica.csv` contiene il template e la selezione iniziale. Il CSV inizia dalle intestazioni: per copiarlo in un nuovo foglio, incollalo a partire da **A2**. Viene usato come bozza soltanto se la scheda Musica non esiste; se la scheda esiste ma è vuota o tutta disattivata, il sito resta senza selezioni e non ripristina la bozza.

L'esportatore genera `musica.json` insieme a `eventi.json` nel workflow esistente. Musica è una scheda editoriale, esclusa dagli eventi e dal sito pubblico finché le novità pubbliche sono disattivate.