# Configurazione
$projectName = "SiBallaImportingData"
$runtime = "osx-arm64" # Cambia in osx-x64 se serve per Intel
$outputDir = ".\bin\Release\net8.0\$runtime\publish"
$zipName = "SiBallaImportingData_Mac.zip"

Write-Host "--- Inizio Compilazione per Mac ---" -ForegroundColor Cyan

# 1. Compilazione (incluso il fix per il feed NuGet che abbiamo visto prima)
<# dotnet publish -c Release -r $runtime --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true --source https://api.nuget.org/v3/index.json
 #>
 dotnet publish -c Release -r $runtime --self-contained true -p:PublishSingleFile=true
 
if ($LASTEXITCODE -ne 0) {
    Write-Host "Errore durante la compilazione!" -ForegroundColor Red
    exit
}

# 2. Copia lo script di avvio nella cartella di pubblicazione prima di zippare
Copy-Item ".\avvia.command" -Destination "$outputDir\avvia.command"

# 3. Creazione file ZIP (più facile da trasferire)
if (Test-Path $zipName) { Remove-Item $zipName }
Compress-Archive -Path "$outputDir\*" -DestinationPath $zipName

Write-Host "--- Completato! Invia il file $zipName al Mac ---" -ForegroundColor Green



# 4. Cosa fare su mac:
<# a. Estrazione
Fai doppio clic sul file .zip. Verrà creata una cartella con i tuoi file all'interno. Sposta questa cartella dove preferisci (ad esempio nella Scrivania o in Applicazioni).

b. Il "Tasto Destro" (Fondamentale)
Poiché l'app non è stata scaricata dall'App Store e non è firmata ufficialmente, se fai doppio clic normale macOS la bloccherà con un errore di sicurezza.

Fai tasto destro (o Ctrl + click) sul file avvia.command.

Seleziona Apri.

Apparirà un avviso che dice: "macOS non può verificare lo sviluppatore di avvia.command".

Clicca sul pulsante Apri (che apparirà solo facendo tasto destro, non con il doppio clic!).

c. Dare i permessi al Terminale (Se richiesto)
Si aprirà una finestra del Terminale. Se macOS ti chiede: "Vuoi consentire a Terminale di accedere ai file nella tua cartella?", clicca su OK. Questo serve per permettere all'app di leggere/scrivere i dati che deve importare.

Cosa succede se avvia.command dà un errore?
A volte macOS è così pignolo che non permette nemmeno allo script di avviarsi. Se ricevi un errore tipo "Permission denied", fai questo (solo una volta):

Apri il Terminale (lo trovi premendo Cmd + Spazio e scrivendo "Terminale").

Scrivi chmod +x  (lascia uno spazio alla fine).

Trascina il file avvia.command dalla cartella dentro la finestra del terminale (scriverà il percorso per te).

Premi Invio.

Ora riprova a fare doppio clic su avvia.command.

Riassunto della struttura finale
Dopo l'avvio, dovresti vedere la finestra del terminale che esegue il tuo codice C#. Se l'app ha bisogno di file di dati esterni, assicurati che siano nella stessa cartella dell'eseguibile.

Un consiglio da amico: Se vedi che l'app si chiude troppo velocemente e non riesci a leggere i log, il file avvia.command che ti ho dato prima ha il comando read alla fine proprio per evitare questo. #>