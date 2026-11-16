using System.Text.Json;

namespace Voice2Laser.Services;

/// <summary>
/// Vocabolario chiuso per il prototipo. Le azioni sono descrittive:
/// nessun tasto viene inviato a Windows/EZCAD2 in questa fase.
/// </summary>
public static class CommandCatalog
{
    public static IReadOnlyList<string> Phrases { get; } =
    [
        "apri file",
        "apri progetto",
        "salva",
        "annulla",
        "stop",
        "conferma",
        "zoom avanti",
        "zoom indietro"
    ];

    public static string GrammarJson =>
        JsonSerializer.Serialize(Phrases.Concat(["[unk]"]));

    public static string Describe(string phrase) => phrase switch
    {
        "apri file" => "Richiesta: apri file",
        "apri progetto" => "Richiesta: apri progetto",
        "salva" => "Richiesta: salva",
        "annulla" => "Richiesta: annulla",
        "stop" => "Richiesta: stop",
        "conferma" => "Richiesta: conferma",
        "zoom avanti" => "Richiesta: zoom avanti",
        "zoom indietro" => "Richiesta: zoom indietro",
        _ => "Comando non associato"
    };
}
