# Resoconto del lavoro — ristrutturazione pipeline MusicBrainz

Periodo: 29/09/2026 23:35 – 30/09/2026 11:50. Branch `LV_claude`. **Nessun commit eseguito.**

> **Stato in sintesi:** codice scritto; baseline prodotta e verificata; pipeline remota
> eseguita con successo. **Il criterio di accettazione non è ancora soddisfatto**: la
> pipeline locale è stata interrotta dal riavvio del PC delle 11:37:59, quindi mancano
> il test di andata e ritorno sui dati completi e i confronti byte per byte degli
> output. Inoltre `C:\mbwork` è finita nel Cestino alle 11:39:20: si veda il §9.

---

## 1. Obiettivo

Ristrutturare il progetto in 3 comandi indipendenti:

1. `fetch_extract.py`: acquisizione, sia remota in streaming sia da archivi locali, ed
   estrazione delle tabelle in CSV in `data_raw/`;
2. `build_genre_graph.py`: legge solo `data_raw/`;
3. `mb_artist_genres.py`: legge solo `data_raw/`.

Logica, formati di output, controlli e report dei due script di elaborazione restano
invariati.

## 2. Discrepanze trovate all'inizio (29/09)

| # | Atteso dal prompt | Stato reale |
|---|---|---|
| D1 | archivi in `dumps/20260926-002121/` | core in `NEW_MB/chat A/dumps/20260926-002121/mbdump.tar.bz2.part`, derived in `NEW_MB/chat B/dumps/mbdump-derived.tar.bz2` |
| D2 | archivi completi | **core troncato**: 2.994.733.056 B su 7.564.971.109 (39,6 %). È un prefisso esatto del file remoto (3 campioni da 1 MB confrontati con HTTP Range) e si interrompe dentro `mbdump/recording` (`EOFError`). Derived completo, SHA256 `c5b5091d…` uguale a SHA256SUMS |
| D3 | output attuali dallo snapshot 20260926 | **provenivano dal 20260923** (TIMESTAMP `2026-09-23 00:21:21.998627+00`, `dump_snapshot = "non determinato"`) |
| D4 | esistono gli output di `mb_artist_genres.py` | non esistevano sul disco |
| D5 | archivi esclusi da git | **erano nell'area di staging** (3,5 GB) e non erano nel `.gitignore` |

Entrambi gli archivi hanno TIMESTAMP `2026-09-26 00:21:21.749766+00`, SCHEMA_SEQUENCE 31
e REPLICATION_SEQUENCE 189282.

## 3. Decisioni approvate

- Il confronto di accettazione si fa sullo **stesso snapshot 20260926**: la baseline
  viene dagli script originali, e il core viene completato in `C:\mbwork`.
- `git rm --cached` sugli archivi.
- Aggiornamento delle frasi dei report diventate false (D6); le modifiche vanno elencate
  tra le differenze spiegate.
- `genres.*` restano sotto git; `artist_genres.csv` è escluso.
- `mbraw.py` contiene **solo** la convenzione CSV (`QUOTE_NOTNULL`) e la lettura di
  `snapshot.json`. Il parser COPY resta in `fetch_extract.py`, e il test lo importa da lì.
- Python ≥ 3.12 (è installato 3.13.3).
- Lavoro e backup in `C:\mbwork\`. Ho verificato che `Documents` **non** è sincronizzata
  da OneDrive.
- Un solo processo pesante alla volta, lanciato come processo separato con log su
  file. Soglia minima di spazio libero: 6 GiB.

## 4. Passo 1a — completamento del core (30/09, 00:07–00:36)

- Ho copiato il `.part` in `C:\mbwork\20260926-002121\mbdump.tar.bz2` (18 s).
- Ho ripreso il download con HTTP Range: risposta `206`, 4.570.238.053 B a ~2,8 MB/s,
  in 27 min.
- **SHA256** `29ea4e7aace435757d82179246942772cf4d44269f5d90d2ece4415603024fe8`, uguale
  a SHA256SUMS ✅.
- I file in `dumps/` non sono stati toccati: dimensione e data di modifica invariate.

## 5. Passo 1b — backup e baseline (00:45–01:10)

- Ho copiato il derived in `C:\mbwork\20260926-002121\` e ne ho verificato lo SHA256.
  Più tardi l'ho cancellato, con il tuo ok, perché duplicato.
- Backup in `C:\mbwork\baseline_20260929\`: gli output attuali di `chat A`, identici a
  git HEAD `c63e3b2`; gli script originali (`mb_artist_genres.py` nella versione in
  staging, blob `d768ed9`); un `MANIFEST.sha256`.
- Primo tentativo di baseline interrotto dalla schermata blu delle 00:50 (§8), poi
  rilanciato come processo separato.

| Comando originale | Durata | Esito |
|---|---|---|
| `build_genre_graph.py --dump C:\mbwork\20260926-002121\mbdump.tar.bz2` | 388 s | 2206 nodi, 3519 archi (subgenre 1590, influenced_by 1749, fusion_of 180), coerenza OK |
| `mb_artist_genres.py --core … --derived "chat B/dumps/…" --genres-csv genres.csv` | 357 s | 213.609 artisti, 469.447 righe |

**`artist_genres.csv` della baseline confrontato con i valori di riferimento: tutti
coincidono.**

| | Riferimento | Baseline |
|---|---|---|
| Dimensione | 46.791.023 B | 46.791.023 B ✅ |
| Righe | 469.447 + intestazione | 469.447 + intestazione ✅ |
| Artisti | 213.609 | 213.609 ✅ |
| SHA256 | `958ac79a…0e55935` | `958ac79a…0e55935` ✅ |
| Fine riga | CRLF | tutte CRLF ✅ |

Confronto della baseline del 26/09 con gli output attuali del 23/09:
- `genres.csv` è **identico**;
- in `genres.graphml` cambiano solo `dump_snapshot` e `dump_timestamp`;
- nel report cambiano solo data, snapshot, TIMESTAMP, percorso, avvisi e tempi.

SHA256 della baseline, che erano registrati nel manifest ora nel Cestino:

```
d4c0829b09a72f53d596592e0c4b0a1f17d9b0d3837f1b97dd3f76ec9e3dc027  genres.csv
e012f18535be6a6c7ea82b6d29e717004b2e0f1c3c1ca4d595aa12e83acb53ea  genres.graphml
acb2a573c333a89c4a8a7e37cde82be841d3d9818b9943a74087c59eee52b744  genres_report.md
958ac79a4d9a252005343ebae5e56a5c6bd3aa3e68354ddbfa39eedcf0e55935  artist_genres.csv
90b8e4e4a2dfd294d16ce32b92558299ff6ef8d021aa54b8f03c655d4c3bdcee  artist_genres_report.md
3a81154cfdb6d7eecd216f9664671393baa96703bb4cb73b63bd684a2a5ee162  ATTRIBUTION.txt
```

## 6. Codice scritto e modifiche al repository

### Operazioni git (nessun commit)

- `git rm --cached` sui due archivi. I file su disco sono intatti.
- `git mv` di `chat A/build_genre_graph.py` e `chat B/mb_artist_genres.py` in `NEW_MB/`,
  e di `chat A/genres.*` in `NEW_MB/output/`.
- `.gitignore` riscritto con fine riga CRLF. Aggiunte: `NEW_MB/data_raw/`,
  `NEW_MB/data_raw.tmp/`, `NEW_MB/data_raw.old/`, `NEW_MB/output/artist_genres.csv`,
  `*.tar.bz2`, `*.tar.bz2.part`. Il file originale non terminava con un a capo: l'ho
  corretto dopo che una riga si era attaccata a `__pycache__/`.
- `README.md` di root: aggiunta una riga che rimanda a `NEW_MB/README.md`.

### `NEW_MB/fetch_extract.py` (nuovo)

**Tabelle e sorgenti**
- `TABLES` in testa al file, con le tabelle non ancora necessarie commentate.
- Archivio e ordine di ogni tabella: `@CORE_TABLE_LIST` e `@DERIVED_TABLE_LIST` di
  `lib/MusicBrainz/Server/Constants.pm` (righe 613 e 852), abbinate agli archivi da
  `admin/ExportAllTables` (righe 353 e 358).
- Nel tar le tabelle seguono l'ordine della lista, precedute dai metadati
  (`MBDump.pm`, righe 147–159). Le tabelle vuote sono omesse (`DatabaseDump.pm`,
  righe 96–113). L'ho verificato empiricamente su 137 tabelle core e 33 derived.
- Colonne: parser dei `CREATE TABLE` di `admin/sql/CreateTables.sql`. È valido perché:
  - il dump usa `COPY $table TO stdout` (`DatabaseDump.pm`, riga 168);
  - l'importatore ufficiale usa `COPY $table FROM stdin` senza elenco di colonne
    (`Utils.pm`, riga 81).
- Corrispondenza con SCHEMA_SEQUENCE: il branch `production` viene risolto nel commit
  `c93f815…`, e lo script controlla `DB_SCHEMA_SEQUENCE` (`DBDefs.pm.sample`, riga 98).
  Opzioni `--schema-ref` e `--schema-dir`. I file usati sono copiati in
  `data_raw/_schema/`.

**Streaming e scrittura**
- Catena HTTPS/file → `bz2` → `tarfile` in modalità `r|`, con **una sola passata** per
  archivio: prima il derived, poi il core. Arresto dopo l'ultima tabella richiesta.
- Log ogni 30 s con i byte letti e la percentuale. Ripresa della connessione con
  `Range`/`If-Range` (5 tentativi).
- Conversione COPY → CSV **riga per riga**, con memoria costante. Su ogni riga controlla
  numero di colonne, UTF-8 e fine riga.
- Controllo dello spazio libero (≥ 6 GiB) all'avvio, prima di ogni tabella e ogni 30 s.
- Scrive in `data_raw.tmp/` e la sostituisce a `data_raw/` solo alla fine. Una `.tmp`
  trovata all'avvio viene segnalata, eliminata e l'estrazione ricomincia da zero.
- `snapshot.json` contiene: snapshot, TIMESTAMP, SCHEMA_SEQUENCE,
  REPLICATION_SEQUENCE, data di estrazione, schema (commit e SHA256 dei file), dati per
  archivio (byte letti, percentuale, arresto anticipato, durata) e dati per tabella
  (righe, colonne, `copy_bytes`, `copy_sha256`, `csv_bytes`).

### `NEW_MB/mbraw.py` (nuovo)

Solo la convenzione CSV e la lettura di `snapshot.json`:
- **NULL** = campo vuoto **senza** virgolette; **stringa vuota** = `""`; ogni altro
  valore tra virgolette (`csv.QUOTE_NOTNULL`, equivalente a PostgreSQL `FORMAT csv,
  FORCE_QUOTE *`). UTF-8, CRLF, intestazione.
- `read_rows()` legge le colonne per nome e controlla intestazione e numero di righe
  rispetto a `snapshot.json`.
- Limite noto: `csv` non sa scrivere una riga composta da un solo campo NULL; in quel
  caso lo script si ferma con un errore esplicito.

### `NEW_MB/tests/test_roundtrip.py` (nuovo)

- Casi sintetici limite: `\N` come testo, TAB, a capo, CR, backslash, caratteri di
  controllo, testo non ASCII, sequenze ottali ed esadecimali.
- Su ogni tabella reale: CSV → testo COPY canonico → confronto di SHA256, byte e righe
  con i valori del dump originale. Conta anche NULL e stringhe vuote.

### `NEW_MB/build_genre_graph.py` (rifattorizzato)

- Rimossi download, SHA256, `read_dump` e il parser COPY. Nuova funzione `read_raw()`
  che legge da `data_raw/`.
- Opzioni `--data-raw` (default `data_raw/`) e `--out-dir` (default `output/`).
- `SCRIPT_VERSION` resta `1.0`, perché è scritta dentro il GraphML.
- **Frasi del report modificate (D6):**
  - «Snapshot (cartella)» diventa «Snapshot»;
  - riga «Acquisizione»;
  - «Lettura dump» diventa «Lettura CSV (data_raw/)»;
  - «Lettura interrotta in anticipo» diventa «Lettura dell'archivio core interrotta in
    anticipo da fetch_extract.py»;
  - decisioni 15, 16, 17 e 19;
  - avviso sullo snapshot non determinato, che ora rimanda a `--snapshot` di
    `fetch_extract.py`.

### `NEW_MB/mb_artist_genres.py` (rifattorizzato)

- Rimossi `scan_archive` e il parser COPY. I gestori ora ricevono righe CSV, con la
  stessa logica di prima. `--genres-csv` è mantenuto.
- Rimossi i controlli «file cambiato tra due letture», che non hanno più senso con una
  sola lettura.
- **Frasi del report modificate (D6):**
  - colonna «File» (nome dell'archivio preso da `snapshot.json`);
  - posizioni delle colonne;
  - decodifica COPY;
  - ordine degli artisti;
  - ordine di lettura;
  - «download automatico non implementato».

### `NEW_MB/README.md` (scritto, **ora mancante**, vedi §9)

Conteneva: uso dei 3 comandi, struttura, requisiti, meccanismo con le citazioni,
convenzione CSV, test, controlli di integrità e licenze.

## 7. Esecuzioni del 30/09 mattina

**Prova rapida** su archivi locali, con `TABLES` ridotta a `genre`, `link_type` e `tag`
e output in `C:\mbwork\smoke`: 328 s. Il test di andata e ritorno è **OK**: SHA256
identico, 2.184 stringhe vuote in `genre` e 229 NULL in `link_type`.

**Pipeline remota** (`fetch_extract.py --snapshot 20260926-002121 --out
C:\mbwork\data_raw_remote`), 10:38–11:31: **exit 0, 3192 s**, picco working set
**36 MiB**, picco di memoria privata 24 MiB.

| Archivio | Byte scaricati | Totale | % | Arresto anticipato | Durata |
|---|---|---|---|---|---|
| derived | 512.753.664 | 519.462.735 | 98,71 | sì (dopo `tag`) | 253 s |
| core | 7.417.626.624 | 7.564.971.109 | 98,05 | sì (dopo `url`) | 2937 s |

| Tabella | Archivio | Righe | CSV (B) | SHA256 del CSV |
|---|---|---|---|---|
| artist | core | 2.995.592 | 454.461.520 | `82c3132ae8532d86…` |
| artist_alias | core | 543.073 | 60.801.964 | `0c58fe0d9ecf7586…` |
| genre | core | 2.206 | 219.541 | `718a69becc2b4767…` |
| l_genre_genre | core | 3.519 | 264.350 | `bde0b428dca3a8f0…` |
| link | core | 1.150.081 | 92.608.421 | `12d846167beff73b…` |
| link_type | core | 697 | 231.548 | `9dd892736d4740d0…` |
| url | core | 21.893.323 | 2.924.894.286 | `377169f7bcdb42a4…` |
| l_artist_url | core | 6.474.142 | 550.944.244 | `7ca748332a2700b2…` |
| tag | derived | 244.571 | 7.529.944 | `e63fd7809c02d354…` |
| artist_tag | derived | 765.754 | 40.573.525 | `0433ff930b27dca7…` |

Gli SHA256 completi e una copia di `snapshot.json` erano in
`C:\mbwork\logs\remote_manifest\`. Poi, con il tuo ok, ho cancellato
`C:\mbwork\data_raw_remote\` per liberare spazio.

**Pipeline locale** (catena lanciata alle 11:31: estrazione → test → due script →
confronto con il remoto): **interrotta** dal riavvio delle 11:37:59 durante
l'estrazione di `l_artist_url`. È rimasta `NEW_MB/data_raw.tmp/` con 6 CSV, di cui
l'ultimo parziale. Alla prossima esecuzione verrà segnalata ed eliminata. In quel punto
i CSV già completi (`artist`, `artist_alias`, `artist_tag`, `genre`, `tag`) avevano la
stessa dimensione di quelli remoti.

## 8. Riavvii e spazio su disco

- **30/09 00:50**: schermata blu (evento 41 con **BugcheckCode 209 = `0xD1`
  `DRIVER_IRQL_NOT_LESS_OR_EQUAL`**, parametri `0x0, 0x2, 0x1, 0xfffff80428e4664b`;
  evento 1001 WER). Dump in `C:\Windows\MEMORY.DMP` (1,5 GB) e in
  `C:\Windows\Minidump\093026-14406-01.dmp`. Non ci sono eventi 1074: non è stato un
  riavvio richiesto. Gli eventi 19 nell'intervallo sono solo installazioni di
  aggiornamenti di Defender e di WindowsAppRuntime.
- **30/09 11:37:59**: riavvio **richiesto dall'utente** dal menu Start (evento 1074,
  «Altro (non pianificato)»).
- **Spazio libero** molto variabile: 37, 32, 16, 6,7, 19 e 22 GiB. In sola lettura non
  ho trovato la causa: i file di sistema visibili sono stabili, e i dati della pipeline
  occupavano al massimo 3,6 GB. File di paging gestito da Windows, 8 GB.
- `MEMORY.DMP`: mi avevi chiesto di cancellarlo, ma **non l'ho cancellato**. Si trova in
  `C:\Windows` e servono i permessi di amministratore; si può rimuovere con *Pulizia
  disco → File dump di memoria degli errori di sistema*.

## 9. Situazione attuale dei file (11:50)

- **`C:\mbwork` è nel Cestino**, spostata alle 11:39:20. Contiene:
  - il core completo verificato;
  - il backup `baseline_20260929\` con la baseline;
  - i log;
  - `remote_manifest\`;
  - gli strumenti `run_measured.py`, `compare_raw.py` e `run_local_chain.sh`.

  **Si può ripristinare dal Cestino.** Senza, per l'esecuzione locale bisogna
  riscaricare il core (il 20260926 è ancora sul mirror insieme al 20260930).
- **`NEW_MB/README.md` e `NEW_MB/analysis/.gitkeep` mancano**. Non sono nel Cestino, e
  la cartella `NEW_MB` risulta modificata alle 11:44. Posso riscriverli.
- `git status`: le modifiche del §6 sono ancora presenti, nessun commit.

## 10. Cosa manca per chiudere il lavoro

1. Ripristinare `C:\mbwork` dal Cestino, o riscaricare il core. Poi ricreare README e
   `.gitkeep`.
2. Rieseguire la catena locale:
   - `fetch_extract.py` sugli archivi locali;
   - `tests/test_roundtrip.py` su tutte e 10 le tabelle;
   - `build_genre_graph.py`;
   - `mb_artist_genres.py --genres-csv output/genres.csv`;
   - confronto degli SHA256 dei CSV locali con il manifest remoto.
3. Confronto byte per byte con la baseline di `genres.graphml`, `genres.csv` e
   `artist_genres.csv`, e elenco di tutte le differenze nei report (quelle attese sono
   date, tempi, percorsi e le frasi D6).
4. Completare il README con i tempi misurati.
5. Commit solo con il tuo ok.
