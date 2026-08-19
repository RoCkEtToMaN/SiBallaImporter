// Modello DTO per la deserializzazione del JSON remoto
using System.Text.Json.Serialization;

public class ComuneDto
{
    [JsonPropertyName("nome")]
    public string Nome { get; set; }

    [JsonPropertyName("codice")]
    public string Codice { get; set; }
}