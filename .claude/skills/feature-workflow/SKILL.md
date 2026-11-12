---
name: feature-workflow
description: Procedura end-to-end per aggiungere una feature in un progetto .NET (modello, logica pura, servizio, endpoint/UI, DI, test, verifica, piano). Usala quando l'utente chiede una nuova funzionalità o un passo di un piano di sviluppo.
---

# Workflow di una feature

Prima di iniziare carica `dotnet-conventions` e la skill di architettura del progetto (`onion-architecture` oppure `aspnet-microservices`), più le eventuali skill di dominio del progetto.

## Procedura

1. **Perimetro.** Individua dati, servizi e file coinvolti e cosa deve cambiare. Se la feature richiede di interpretare un formato o un'API esterna, il primo deliverable è il parser o il client, con un test su dati reali.
2. **Modello.** Aggiungi record, enum e DTO; nessuna logica.
3. **Logica pura.** Implementa algoritmi e regole in classi senza I/O (SubService o classi pure) e scrivi subito i test:
   - golden: input reale → output atteso;
   - roundtrip, quando applicabile: parse → serialize → dati identici;
   - robustezza: input troncati o corrotti → errore esplicito.
4. **Servizio.** Orchestra i passi. Per le operazioni distruttive l'ordine è obbligatorio:
   1. backup o verifica che l'operazione sia ripetibile;
   2. load;
   3. modifica;
   4. validazione;
   5. write.

   In caso di errore l'originale non deve mai essere sovrascritto. Nei job asincroni: accoda, aggiorna lo stato, rendi il job annullabile (vedi `aspnet-microservices`).
5. **Errori.** Ogni nuova situazione d'errore ha un nuovo codice in `ErrorCodes` (aggiorna anche il catalogo condiviso con il frontend).
6. **DI.** Registra interfacce e implementazioni nella composition root.
7. **Endpoint / UI.** Controller che delegano al servizio e restituiscono DTO tipizzati, oppure ViewModel e View dedicati. Le conferme e la scelta dei file passano da un servizio di dialogo.
8. **Verifica (solo automatica: le prove manuali le fa l'utente).**
   - `dotnet build <solution>` senza warning;
   - `dotnet test <solution>` tutto verde;
   - test d'integrazione sul flusso completo con dati reali o di esempio;
   - **non** avviare l'app e non toccare dati reali dell'utente: a fine lavoro indica all'utente cosa provare a mano.
9. **Piano.** Aggiorna lo stato nel documento di piano del progetto, se esiste (`docs/` o `PIANO_*.md`).
10. **Documentazione.** README, catalogo degli errori per la UI e guida di deploy (servizi, code, collection nuove o rinominate).

## Piani di lavoro (feature grandi o cambi di architettura)

- Prima del codice, un documento `PIANO_<TEMA>.md` nella radice del repository con:
  - obiettivo e situazione attuale (dove sta oggi la logica);
  - una tabella **Decisioni** numerate (D1, D2, …) con la proposta consigliata;
  - l'architettura (servizi, dati, messaggi, endpoint);
  - le **fasi** in tabella (contenuto + come si verifica), ognuna rilasciabile da sola;
  - i rischi e le domande aperte.
- Le decisioni si chiedono all'utente con domande a scelta, l'opzione consigliata per prima. Le risposte vanno nel piano; se l'utente cambia idea, il piano si aggiorna prima del codice.
- Una fase completata si segna con ✅ e una nota sulle differenze rispetto al piano. Le scelte prese durante l'implementazione vanno nel piano, non solo nel messaggio.
- Se l'utente dice "salva il piano ma non procedere", non si scrive codice.
- Le rinomine (servizi, progetti, namespace) sono una fase a sé, a parità di comportamento, prima delle modifiche funzionali.

## Dipendenze esterne

- Prima di basare un flusso su un'API di terze parti, verifica con una ricerca aggiornata: quote, limiti di utenti in modalità sviluppo, scope, termini d'uso, webhook o push disponibili. Le condizioni cambiano spesso; non fidarti della memoria e cita le fonti all'utente.
- Preferisci le integrazioni che non dipendono da un unico fornitore limitato (es. un aggregatore che riceve i dati dal fornitore lato server) e tieni sempre un'importazione manuale (export) come riserva.

## Strumenti (Windows / PowerShell 5.1)

- Per modifiche di testo usa il tool Edit; negli script PowerShell:
  - non chiamare una funzione `R` (è l'alias di `Invoke-History`) né `Enum` (parola chiave): usa nomi come `Rep` o `Sub`;
  - leggi e scrivi con `[IO.File]::ReadAllText/WriteAllText` e `UTF8Encoding($false)`, normalizzando `\r\n` → `\n` prima dei confronti;
  - `-ne` e `-eq` ignorano maiuscole e minuscole: per i confronti esatti usa `[string]::Equals(a, b, 'Ordinal')`;
  - un array di una sola coppia viene "srotolato": avvolgilo con `,@(...)` o usa una funzione per coppia.
- Prima di modifiche estese fai un backup mirato dei soli sorgenti (escludi bin, obj, node_modules, dump): copiare tutto il repository può richiedere minuti.
- Angular: i test si lanciano con `ng test --watch=false`; `vitest` da solo non carica il setup di Angular. Il budget di stile per componente (4 kB) si rispetta dividendo gli stili in più file (`styleUrls`).

## Regole
- Ogni comportamento fuori standard (non coperto dalle skill o in deroga a una regola) va chiesto all'utente prima di implementarlo. Se approvato, va aggiunto alla skill generica pertinente (vedi `dotnet-conventions` › Comportamenti fuori standard).
- Nessuna modifica distruttiva senza backup o idempotenza.
- Una feature non è finita senza almeno un test di regressione.
- Porta prima gli algoritmi puri, poi i casi d'uso, poi endpoint e UI. Non copiare 1:1 la struttura di codice legacy.
- Spostare logica tra linguaggi o tra frontend e backend: genera i golden dall'implementazione originale con uno script, confronta campo per campo con tolleranza, e solo dopo rimuovi l'originale (vedi `dotnet-conventions` › Porting numerico). Le fixture restano come riferimento congelato.
- A fine lavoro dichiara cosa **non** è stato verificato (app non avviata, integrazioni reali non provate).

## Definition of Done

- Build verde e test passati.
- Flusso completo funzionante sui dati di test.
- Codici d'errore e documentazione aggiornati.
- Indicazioni all'utente sulle verifiche manuali da fare.
