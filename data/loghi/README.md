# Loghi degli organizzatori

Per le righe future dello Sheet che non hanno un'immagine dell'evento (`no` nella colonna
**Immagine**), il programma usa il logo dell'organizzatore preso da questa cartella: lo carica su
Appwrite Storage e scrive `logo` nella colonna. Gira insieme a `CercaImmaginiSheet`
(`--immagini-sheet`, Variable di GitHub `CERCA_IMMAGINI_SHEET`).

Regole:

- un file per organizzatore, **jpg, png o webp**;
- il nome è organizzatore e città come nello Sheet, in minuscolo, senza accenti, spazi e simboli
  sostituiti da `-`, con `_` tra organizzatore e città (es. `simone-scanzoni_roma.jpg`). Se il nome
  è sbagliato il logo non viene usato: il log del giro elenca i loghi caricati e le righe aggiornate;
- immagine quadrata o quasi, sotto 1 MB (es. l'immagine del profilo della pagina);
- se sostituisci un file con un'immagine diversa, al giro dopo viene ricaricato;
- una riga con `logo` non viene più ricercata: per riprovare con il sito della riga, svuota la cella.

I file vanno nel repo (commit e push), così li trova anche il giro automatico su GitHub.

## Organizzatori con più righe senza immagine (al 4/11/2026)

| # | Organizzatore | Città | Righe | Pagina | Nome del file |
|---|---|---|---|---|---|
| 1 | Simone Scanzoni | Roma | 27 | https://www.facebook.com/simone.scanzoni | `simone-scanzoni_roma.jpg` |
| 2 | Wham - Swing Vibes Brescia | Brescia | 26 | https://www.facebook.com/whamswingbrescia | `wham-swing-vibes-brescia_brescia.jpg` |
| 3 | Crazy Ballet | Reggio Emilia | 25 | https://www.facebook.com/crazyballet | `crazy-ballet_reggio-emilia.jpg` |
| 4 | Turin Cats | Torino | 15 | https://go.turincats.com/whatsapp | `turin-cats_torino.jpg` |
| 5 | Blue Energy Rock | Verona | 13 | https://www.facebook.com/BlueEnergyRock | `blue-energy-rock_verona.jpg` |
| 6 | Jazz Road - Swing Dance | Pisa | 13 | https://www.facebook.com/jazzroadswingdance | `jazz-road-swing-dance_pisa.jpg` |
| 7 | Sylvain Gautier | Ile-de-France | 11 | https://www.facebook.com/monsieur.gautier | `sylvain-gautier_ile-de-france.jpg` |
| 8 | Sylvain Pelé | Ile-de-France | 11 | https://www.facebook.com/sylvain.pele | `sylvain-pele_ile-de-france.jpg` |
| 9 | The Zenaswingers - Swing a Genova | Genova | 10 | https://www.facebook.com/zenaswingersofficialpage | `the-zenaswingers-swing-a-genova_genova.jpg` |
| 10 | MoveT Aps | Reggio Emilia | 9 | https://www.facebook.com/MoveT.aps | `movet-aps_reggio-emilia.jpg` |
| 11 | Garage Hoppers | Genova | 8 | https://www.facebook.com/profile.php?id=61569179646341 | `garage-hoppers_genova.jpg` |
| 12 | Alain Tolet | Brussels-Capital | 8 | https://lestompingatsaint-gilles.eo.page/mzxck | `alain-tolet_brussels-capital.jpg` |
| 13 | Loreto Lilou | Metropolitan France | 8 | https://www.helloasso.com/associations/loreto-et-lilou/evenements/sunday-swing-rdv-wcs-29 | `loreto-lilou_metropolitan-france.jpg` |
| 14 | Brescia Balboa Series | Brescia | 7 | https://www.facebook.com/profile.php?id=61588689733980 | `brescia-balboa-series_brescia.jpg` |
| 15 | Gemma Club Aps | Milano | 6 | https://www.facebook.com/GemmaClubA.P.S | `gemma-club-aps_milano.jpg` |
| 16 | GIPSY SWING | Torino | 6 | https://www.facebook.com/GipsySwingDanceSchool | `gipsy-swing_torino.jpg` |
| 17 | Swing Crew Wien | Vienna | 6 | https://www.facebook.com/swingcrewwien | `swing-crew-wien_vienna.jpg` |
| 18 | Your Swing | Genova | 6 | https://www.facebook.com/YourSwingItaly | `your-swing_genova.jpg` |
| 19 | Virginie Escaravage e Thomas Escaravage | Ile-de-France | 5 | http://www.newdancegeneration.com | `virginie-escaravage-e-thomas-escaravage_ile-de-france.jpg` |
| 20 | Blues Dance Genova | Genova | 5 | https://www.facebook.com/profile.php?id=100089193216434 | `blues-dance-genova_genova.jpg` |
