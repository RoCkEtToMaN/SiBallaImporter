# Immagini degli eventi messe a mano

Per un evento che non ha immagine (o ne ha una sbagliata), metti qui l'immagine: al giro dopo il
programma la carica su Appwrite Storage al posto di quella trovata online e scrive `sì` nella colonna
**Immagine** dello Sheet. Gira insieme a `CercaImmaginiSheet` (`--immagini-sheet`, Variable di GitHub
`CERCA_IMMAGINI_SHEET`).

Regole:

- un file per evento, **jpg, png o webp**, sotto 10 MB (meglio sotto 1 MB);
- il nome è **data di inizio** (`aaaa-mm-gg`), `_`, **titolo** della riga dello Sheet, es.
  `2026-11-20_swing-sul-mare.jpg`. Nel titolo maiuscole, accenti, simboli, numeri e anni non contano:
  `2026-11-20_Swing sul mare.jpg` va bene lo stesso;
- se il nome non corrisponde a nessuna riga futura, il log del giro lo segnala
  (`nessuna riga dello Sheet il ... con titolo ...`);
- se sostituisci un file con un'immagine diversa, al giro dopo viene ricaricato;
- vale per tutte le righe con quella data e quel titolo (Serate e Workshop) e ha la precedenza
  sull'immagine trovata online e sul logo dell'organizzatore;
- se cambi data o titolo della riga nello Sheet, rinomina anche il file;
- passato l'evento il file viene ignorato e si può cancellare.

Per un'immagine che vale per tutti gli eventi di un organizzatore usa invece `data/loghi/`.

I file vanno nel repo (commit e push), così li trova anche il giro automatico su GitHub.
