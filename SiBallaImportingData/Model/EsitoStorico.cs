namespace SiBallaImportingData.Model
{
    /// <summary>Esito del controllo con Gemini di una riga "storico" (--riclassifica-storico)</summary>
    public enum EsitoStorico
    {
        Swing,          // resta "storico", nota "Gemini: swing"
        No,             // diventa "no", motivo nella nota
        DaControllare   // Gemini non ha abbastanza informazioni: resta "storico", nota "Gemini: da controllare - motivo"
    }
}
