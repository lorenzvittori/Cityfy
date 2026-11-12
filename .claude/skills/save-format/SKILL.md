---
name: save-format
description: Formato binario dei save Cyberpunk 2077 (sav.dat) come implementato e verificato in questo progetto — header, tabella chunk FZLC/LZ4, forma decompressa, node table EDON/ENOD, nomi dei nodi reali. Usala prima di toccare decompressione, ricompressione, deserializzazione, merge o parsing di sezioni.
---

# Formato save Cyberpunk 2077

Implementazione: `src/Cyberpunk-Save-Elaborator.Application/Internal/CompressionTable.cs`, `SubServices/SaveDecompressionSubService.cs`, `SubServices/SaveRecompressionSubService.cs`, `SubServices/SaveDeserializationSubService.cs`. Costanti: `Domain/Models/CyberSaveConstants.cs`.

## Layout file compresso (`sav.dat`)

```
[0 .. tablePos)        header, inizia con "VASC" (tablePos = 25 sul save di test, ma va SEMPRE cercato con SeekMagicBytes("FZLC"))
[tablePos]             "FZLC" + int32 chunkCount
                       chunkCount × (int32 offset, int32 compressedSize, int32 decompressedSize)   offset assoluti
                       padding a zero fino a TableEntriesCount voci (0x100 → chunk 256 KB, 0x400 → chunk 512 KB)
[chunks[0].Offset ..]  chunk: "4ZLX" + int32 decompressedSize + payload LZ4 block  (compressedSize include gli 8 byte di header)
                       oppure chunk raw non compresso (compressedSize == decompressedSize)
[fine ultimo chunk ..] footer: node table "EDON" ... + int32 nodeTableOffset + "ENOD"
```

`TableEntriesCount = (chunks[0].Offset - tablePos - 8) / 12`.

## Forma decompressa canonica

È quella su cui lavorano deserializzazione e merge (`CyberSaveDocument.DecompressedBytes`):
- stesso header;
- tabella FZLC con chunk **non compressi**, ridivisi a `ChunkSize`;
- dati in chiaro;
- footer copiato, con gli ultimi 8 byte riscritti: `nodeTableOffset` = nuova posizione del footer.

Gli `Offset` dei nodi si riferiscono alla forma decompressa.

## Node table (dopo "EDON")

`packedInt nodeCount`, poi per ogni nodo: `packedString name, int32 nextId, int32 childId, int32 offset, int32 size`.
- Albero tramite indici: `childId` = primo figlio, `nextId` = fratello successivo, `-1` = nessuno.
- `packedInt`: byte 0 → bit 0-5 valore, bit 6 = continua, bit 7 = segno negativo; i byte successivi portano 7 bit ciascuno, con bit 7 = continua.
- `packedString`: lunghezza negativa → 1 byte per carattere; positiva → UTF-16 (2 byte per carattere).

## Garanzie verificate (test in tests/.../SaveCompressionRoundtripTests.cs)

- decompress → recompress del save di test produce **lo stesso file byte per byte**;
- decompress → recompress → decompress è stabile e la node table resta identica;
- input corrotti (header mancante, file troncato, 0 chunk, payload LZ4 rotto, footer rotto) → `InvalidDataException`.

Qualsiasi modifica alla pipeline deve mantenere verdi questi test.

## Nodi reali (save di test: 1946 nodi)

| Sezione editor | Nodi da cui legge e scrive (accesso in `Application/Internal/SaveSessionAccess.cs`) |
|---|---|
| Inventario, Eddies | `inventory` (1722 figli `itemData`; gli eddies sono l'item `Items.money`), più `UniqueItemCounter` e `StatsSystem` per gli item creati |
| Livelli | `ScriptableSystemsContainer` (PlayerDevelopmentSystem), `StatsSystem` (forced modifier del giocatore) |
| Equipaggiamento | `ScriptableSystemsContainer` (EquipmentSystem) |
| Quest flag | `questSystem` → `FactsDB` → `FactsTable` ×10 |
| Veicoli | `PersistencySystem2` → `vehicleGarageComponentPS` |
| Extra | `ScriptableSystemsContainer` (FastTravelSystem), `godModeSystem` |

Altri nodi: `ItemDropStorageManager` (figli `ItemDropStorage` → `itemData`), `WardrobeSystem`, `StatPoolsSystem`, `PlayerSystem`, `JournalManager`.

Altri nodi rilevanti: `CharacetrCustomization_Appearances` (typo originale del gioco, non correggerlo), `PersistencySystem2` (~10 MB), `ContainerManager`.

`itemData` non è univoco: esiste anche sotto `ItemDropStorage`. Per i figli di un nodo segui la catena `childId`/`nextId`, non filtrare per nome.

## Motore WolvenKit (lettura e scrittura tipizzate)

Dalla fase F0 i save si aprono e si scrivono con WolvenKit (`external/WolvenKit` + `patches/WolvenKit.patch`). La nostra pipeline sopra resta per la vista a basso livello (node table, strumenti) e per i test.
- API: `CyberpunkSaveReader.ReadFile(out CyberpunkSaveFile)` (`EFileReadErrorCodes` in `WolvenKit.RED4.Save.IO`: `NoError`, `NoCSav`, `UnsupportedVersion` per save precedenti alla 2.0), `ReadFileInfo` per l'header, `CyberpunkSaveWriter.WriteFile(save, true)`. `CompressionSettings.Get().UseOodle = false`.
- Header (`CyberpunkSaveHeaderStruct`): `SaveVersion`, `GameVersion` (2500 = patch 2.5), `GameDefPath`, `TimeStamp`, `ArchiveVersion`. Il save di test: game 2500, save 277, archive 195.
- Accesso ai dati (come `SaveFileHelper` di SimpleGUI): `save.Nodes.First(n => n.Name == "inventory").Value` → `Inventory` (`SubInventories`, `Items`, `ItemData.Quantity`; soldi = `Items.money` nel sub-inventory 1); `ScriptableSystemsContainer` → `Package.Content` (`RedPackage`) → `Chunks` → `PlayerDevelopmentSystem.PlayerData[OwnerID.Hash == 1]` (proficiency, attributi, `DevPoints`, `LifePath`); `StatsSystem` → `gameStatsStateMapStructure` (formato ottimizzato 2.5: serve `OptimizedStatsHelper` e `PrepareForSave` prima di scrivere).
- Valori del save di test: 17 sub-inventory, 5.997.148 eddies, livello 80, Street Cred 50, attributi a 20, lifepath Corporate.
- Prestazioni misurate: primo parse ~21 s (di cui ~6 s costruttore statico di `RedReflection`, anticipato all'avvio da `SaveEngineWarmupService`), parse successivi ~9 s, scrittura ~4 s, picco RAM 2-3 GB.
- **Non thread-safe**: due parse concorrenti falliscono → sempre dentro `WolvenKitGate.Lock` (`System.Threading.Lock`, rientrante).
- Statistiche giocatore (F1, `Application/Internal/SaveSessionAccess.cs` + `OptimizedStatsHelper.cs`): `PlayerDevelopmentData` contiene `Attributes[].Value`, `Proficiencies[].CurrentLevel/MaxLevel/CurrentExp`, `DevPoints[].Unspent` (Attribute, Primary = perk, Espionage = reliquia), `LifePath`. Il gioco tiene una copia di attributi e proficiency come forced modifier costanti nelle stat dell'entità 1: vanno aggiornati insieme. Sui save 2.5 le stat sono in `ValuesOptimized`/`Shared*`: si modificano tramite la vista di `OptimizedStatsHelper` e si riportano nel save con `CommitPendingStats()` (chiamato da `SavePrepareSubService`) prima di serializzare.
- Riscrittura del save non modificato: byte-identica all'originale (`SaveGameRoundtripTests`).
- Inventario (F2):
  - `Inventory.SubInventories[]` (`InventoryId`, `Items`), con `ItemData.ItemInfo.ItemId` = `gameItemID` (`Id` TweakDBID, `RngSeed`, `UniqueCounter`), `Quantity`, `Flags` (`IsQuestItem`, `IsNotUnequippable`);
  - `ItemStructure`: None/Extended/Quantity; `RngSeed == 2` = item a istanza singola con quantità;
  - item creati: `UniqueItemCounter.Count` incrementato per ogni id nuovo; armi e vestiti hanno stat proprie (`gameStatsObjectID` di tipo ItemID con `InventoryHelper.GetItemIdHash`);
  - equipaggiamento: `EquipmentSystem.OwnerData[OwnerID.Hash == 1].Equipment.EquipAreas[].EquipSlots[].ItemID`. Lo **stesso item compare in più aree**: Weapon + WeaponWheel, cyberware IntegumentarySystemCW + QuickWheel, braccio ArmsCW + BaseFists + WeaponWheel. Slot vuoto = `gameItemID` con `Id` 0;
  - albero mod: `ItemData.ItemSlotPart` (radice = parte base) → `Children[]`, ogni nodo con `AttachmentSlotTdbId` e `ItemInfo.ItemId`; i mod nuovi hanno seed e contatore univoci e `RequiredLevel = float.MaxValue`;
  - qualità: modifier costante `Quality` (4 = leggendario) nelle stat dell'item, più `RandomCurveInput = 0` di tipo Multiplier.
- Aspetto (F5):
  - il nodo `CharacetrCustomization_Appearances` contiene `gameuiCharacterCustomizationPresetWrapper` (`IsBrainGenderMale` = voce, `Preset.IsMale` = corpo, `HeadGroups`/`ArmsGroups`/`BodyGroups` → `Customization[]` con `Name`, `Definition`, `Resource`);
  - il genere del corpo sta anche in `PersistencySystem2.Entries[].Data` di tipo `PlayerPuppetPS` → `Gender` (CName "Male"/"Female"), da cambiare insieme all'aspetto;
  - i preset .v2preset sono `{"JsonVersion": "0.0.9", "Data": <wrapper>}` serializzati con `RedJsonSerializer` (WolvenKit.Common).
- Fact delle quest (F4): `FactsTable.FactEntries[]` con `FactName` (`Fact`, hash FNV-1a 32 bit del nome) e `Value` (uint). Un fact sconosciuto al catalogo `Facts.json` esiste nel save solo come hash. WolvenKit riordina le voci per hash quando scrive; i fact nuovi vanno nella prima tabella, come fa SimpleGUI.
- Veicoli ed extra (F6):
  - garage: `vehicleGarageComponentPS.UnlockedVehicleArray[]` (`VehicleID.RecordID`, `Health`); il save di test ha **voci duplicate** (30 voci, 26 veicoli): per bloccare un veicolo vanno tolte tutte; i nuovi entrano con `Health = 100`;
  - viaggio rapido: chunk FastTravelSystem → `FastTravelNodes[]` di `CHandle<gameFastTravelPointData>` (`PointRecord`, `MarkerRef`, `IsEP1`); save di test: 148 punti, 5 di Phantom Liberty;
  - god mode: `godModeSystem` → `Package` → RootChunk `gameGodModeSaveData` → `Gods[EntityId.Hash == 1].Data.Overrides[]` (`Type` = `gameGodModeType`: Invulnerable/Immortal/Mortal); il save di test ha Immortal sul giocatore.
- Nomi TweakDB: `TweakDBID.GetResolvedText()` restituisce null finché `TweakDbNames.EnsureLoaded()` non ha popolato il pool. Il confronto `id == "Items.money"` invece funziona sempre, perché calcola l'hash.

## Cosa NON è ancora implementato

- Editing tipizzato dei nodi in UI: WolvenKit li legge già tutti, i SubService per statistiche, inventario, fact, aspetto ecc. arrivano dalla F1 in poi (`docs/Feature-Parity-Plan.md`).
- Merge personaggio (F8): non copia mai byte o nodi tra save. Legge i dati tipizzati dei due save e li applica al target con i SubService delle sezioni. Gli item nuovi sono ricreati dal catalogo (nuovi seed e contatori, quindi nuovo `gameStatsObjectID`), poi ricevono una copia (`DeepCopy`) dei modificatori dell'istanza del sorgente: sono valori senza riferimenti ad altri oggetti.
- Rarità degli item 2.x (misurata sul save di test, 685 item con stat): quasi mai c'è un `Quality` costante. Contano `IsItemPlus` (0/1/2), `Quality` relativo al record (es. -4 su un record leggendario dà un item comune), `ItemLevel`, `LootLevel`, `WasItemUpgraded` e i modificatori Curve/Combined. Per conservare la rarità vanno copiati tutti, **insieme a `RngSeed` e `ItemAdditionalInfo.LootItemPoolId`** (358 item su 1256 hanno una tabella di loot): il gioco ricalcola la qualità da tabella di loot + seed, quindi con le sole stat l'item resta comune (verificato dall'utente in gioco).

## Ispezionare un save rapidamente

File-based app .NET 10 (in una cartella temporanea, non nel repo):

```csharp
#:project <repo>/src/Cyberpunk-Save-Elaborator.Application/Cyberpunk-Save-Elaborator.Application.csproj
using Cyberpunk_Save_Elaborator.Application.SubServices;
var c = File.ReadAllBytes(args[0]);
var d = new SaveDecompressionSubService().Execute(c);
var (meta, nodes) = new SaveDeserializationSubService().Execute(c, d);
for (var i = 0; i < nodes.Count; i++) Console.WriteLine($"#{i} {nodes[i]}");
```

`dotnet run probe.cs -- <path>/sav.dat`
