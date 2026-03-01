#!/bin/bash
# Vai nella cartella dove si trova questo script
cd "$(dirname "$0")"

# Dai i permessi all'eseguibile (automaticamente)
chmod +x ./SiBallaImportingData

# Avvia l'app
./SiBallaImportingData

# Messaggio finale per non chiudere subito la finestra se crasha
echo ""
echo "Esecuzione terminata. Premi un tasto per uscire."
read