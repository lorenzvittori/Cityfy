---
name: porting-feature
description: Workflow per aggiungere o portare da CyberCAT una feature dell'editor save (lettura/edit di una sezione, scrittura, merge personaggio) end-to-end attraverso Domain, Application, UI e test. Usala quando l'utente chiede una nuova funzionalità sul save o un passo del piano di Fase 2.
---

# Porting di una feature (CyberCAT → .NET 10)

Contesto: clone moderno di CyberCAT. Riferimento principale per le funzioni: Ishaxse/CyberCAT-SimpleGUI (save 2.0-2.5, usa WolvenKit GPL-3.0 per leggere e scrivere); riferimento storico: WolvenKit/CyberCAT (archiviato nel 2022, formato pre-2.0). Obiettivo: parità funzionale più la feature extra "merge personaggio da secondo save". Piano e stato: `docs/Phase2-Plan.md` (stream generali) e `docs/Feature-Parity-Plan.md` (fasi F0-F8, mappa funzione CyberCAT → file sorgente originale).

Prima di iniziare carica anche le skill `onion-architecture` (layer e naming) e `save-format` (formato binario).

## Procedura

1. **Perimetro.** Individua la sezione o i nodi coinvolti (tabella in `save-format`) e cosa deve cambiare nel file. Se la feature richiede di interpretare il contenuto di un nodo, il primo deliverable è il parser, con test sul save reale.
2. **Domain.** Aggiungi record o enum per i dati della sezione in `Domain/Models`. Niente logica.
3. **SubService.** Implementa la logica pura (parser, mapper, validator, composer) in `Application/SubServices` e scrivi subito i test:
   - golden: input `tests/.../TestData/sav.dat` → output atteso;
   - roundtrip: parse → serialize → byte identici, quando il nodo non viene modificato;
   - robustezza: dati troncati o corrotti → `InvalidDataException`.
4. **Service.** Orchestra i passi in `Application/Services`, ritorna `*Result.Success/Failure`. Per le scritture l'ordine è obbligatorio:
   1. backup del target (`ISaveBackupSubService`, file `<nome>.old.<timestamp>`);
   2. load: decompress + deserialize;
   3. modifica;
   4. validazione;
   5. recompress;
   6. write.

   In caso di errore il file originale non deve mai essere sovrascritto.
5. **DI.** Registra interfacce e implementazioni in `src/Cyberpunk-Save-Elaborator.UI/App.xaml.cs`.
6. **UI.**
   - ViewModel della sezione derivato da `ObservableSection`, con `Load(SaveSession?)` e `SessionModified` dopo ogni operazione riuscita, più la View dedicata in `Views/Sections` con il suo `DataTemplate` in `MainWindow.xaml`.
   - La sessione è `MainWindowViewModel.CurrentSession`. Le sezioni che condividono dati si rileggono a vicenda tramite `SessionModified` (es. Inventario ↔ Eddies ↔ Equipaggiamento).
   - Mantieni sia i preset che la configurazione manuale.
   - Conferme e scelta file passano da `IUserDialogService`.
7. **Verifica (solo automatica: le prove manuali le fa l'utente).**
   - `dotnet build Cyberpunk-Save-Elaborator.slnx` senza warning (`$env:GitExe` se git non è nel PATH);
   - `dotnet test Cyberpunk-Save-Elaborator.slnx` tutto verde;
   - test d'integrazione sul save reale con `ParsedSampleSaveFixture.ParseFresh()`: modifica → `SaveWriteService` (con `InMemorySaveFileClient`) → `ParsedSampleSaveFixture.Parse(bytes)` → valori nuovi presenti e resto invariato; "applica senza modifiche" deve lasciare il save byte-identico;
   - test del ViewModel con un Service finto, e la nuova View aggiunta a `XamlViewsTests`;
   - **non** avviare l'app né toccare `Saved Games`: a fine fase indica all'utente cosa provare a mano e in gioco.
8. **Piano.** Aggiorna lo stato in `docs/Phase2-Plan.md`.

## Regole

- Nessuna modifica distruttiva senza backup.
- Ogni feature ha almeno un test di regressione prima di considerarla finita.
- La compatibilità dell'output con il gioco è un requisito bloccante: un save scritto deve ricaricarsi nel tool e, per le feature di scrittura, va segnalato all'utente che serve la verifica in gioco (non verificabile da qui).
- Non copiare 1:1 la struttura legacy di CyberCAT; porta prima gli algoritmi puri, poi i casi d'uso, poi la UI.
- Rischi noti: strutture dipendenti dalla versione del gioco, dipendenze interne tra inventario, equip e statistiche, offset non documentati.

## Definition of Done

- Build verde .NET 10 e test passati.
- Flusso load → edit → save funzionante sul save di test.
- Save risultante ricaricabile dal tool. La verifica in gioco la fa l'utente.
- `docs/Phase2-Plan.md` aggiornato.
