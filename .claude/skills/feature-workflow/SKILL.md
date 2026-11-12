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
9. **Piano.** Aggiorna lo stato nel documento di piano del progetto, se esiste (`docs/`).

## Regole

- Ogni comportamento fuori standard (non coperto dalle skill o in deroga a una regola) va chiesto all'utente prima di implementarlo. Se approvato, va aggiunto alla skill generica pertinente (vedi `dotnet-conventions` › Comportamenti fuori standard).
- Nessuna modifica distruttiva senza backup o idempotenza.
- Una feature non è finita senza almeno un test di regressione.
- Porta prima gli algoritmi puri, poi i casi d'uso, poi endpoint e UI. Non copiare 1:1 la struttura di codice legacy.

## Definition of Done

- Build verde e test passati.
- Flusso completo funzionante sui dati di test.
- Codici d'errore e documentazione aggiornati.
- Indicazioni all'utente sulle verifiche manuali da fare.
