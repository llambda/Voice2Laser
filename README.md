# Voice2Laser

Applicazione Windows per il riconoscimento **offline** di comandi vocali in italiano destinati a EZCAD2.

## Stato del progetto

**Fase 1 – prototipo di ascolto:** acquisizione microfono continua, riconoscimento Vosk con grammatica limitata, visualizzazione dei risultati e registro eventi. In questa fase il programma **non invia tasti a EZCAD2**.

La separazione tra acquisizione audio, motore ASR e gestione comandi permette di sostituire o affiancare Vosk (ad esempio con sherpa-onnx) dopo i test nel rumore reale.

## Requisiti

- Windows 10 64 bit
- Visual Studio 2022 con workload **Sviluppo desktop .NET**
- .NET 8 SDK
- Microfono riconosciuto da Windows

## Avvio

1. Clona il repository.
2. Apri `Voice2Laser.sln` in Visual Studio.
3. Ripristina i pacchetti NuGet e compila.
4. Scarica il modello italiano piccolo Vosk: [vosk-model-small-it-0.22](https://alphacephei.com/vosk/models).
5. Estrai il contenuto in `models/vosk-model-small-it-0.22` nella cartella del progetto.
6. Avvia l'applicazione e premi **Avvia ascolto**.

In alternativa, imposta il percorso del modello dalla casella nell'interfaccia. Il modello non è incluso nel repository.

## Comandi iniziali

La grammatica iniziale è volutamente contenuta e modificabile in `src/Voice2Laser/Services/CommandCatalog.cs`:

- apri file
- apri progetto
- salva
- annulla
- stop
- conferma
- zoom avanti
- zoom indietro

Questi sono comandi dimostrativi: prima dell'uso operativo dovremo verificare quali scorciatoie sono effettivamente configurate nella versione di EZCAD2 utilizzata.

## Note audio

Il prototipo acquisisce audio a 16 kHz, mono, PCM 16 bit. La qualità nel rumore dipende soprattutto dal microfono, dalla sua posizione e dall'ambiente: non è possibile garantire il riconoscimento corretto senza prove sul posto. Per il laboratorio è preferibile una cuffia wireless con microfono boom direzionale e dongle USB 2,4 GHz.

## Sicurezza

- Il prototipo non controlla il laser e non invia input a EZCAD2.
- Non usare il riconoscimento vocale come dispositivo di sicurezza.
- Avvio marcatura, emissione laser e altre azioni potenzialmente pericolose dovranno richiedere una conferma separata e restare subordinate a interblocchi e arresto di emergenza hardware indipendenti.

## Struttura

```
src/Voice2Laser/
  Models/                 Modelli dati
  Services/               Audio, Vosk e catalogo comandi
  ViewModels/             Stato e logica UI
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs
  Voice2Laser.csproj
```

## Prossime tappe

1. Verificare acquisizione e riconoscimento con il microfono reale.
2. Registrare campioni nel rumore effettivo e misurare falsi riconoscimenti.
3. Aggiungere soglie, cooldown e conferma dei comandi.
4. Solo dopo i test, implementare l'invio controllato di scorciatoie Windows a EZCAD2.
