// User-facing messages for the backend error codes (catalog: ERRORI_UI.md at the repo root).
// The UI decides only on the code, never on the technical "detail".
// A .NET test (ServiceDefault.Tests) fails if a backend code is missing here.

/** What the UI offers next to the message. */
export type ErrorAction = 'retry' | 'login' | 'relink' | 'export-guide' | 'upload' | 'home' | 'none';

export interface ErrorMessage {
  message: string;
  action: ErrorAction;
}

const MESSAGES: Record<string, ErrorMessage> = {
  // --- General
  INTERNAL_ERROR: { message: 'Qualcosa è andato storto. Riprova tra poco.', action: 'retry' },
  SERVICE_UNAVAILABLE: { message: 'Il servizio non è raggiungibile in questo momento. Riprova tra qualche minuto.', action: 'retry' },
  VALIDATION_FAILED: { message: 'Alcuni dati non sono validi. Controlla e riprova.', action: 'none' },
  UNAUTHENTICATED: { message: 'Inizia per creare la tua città.', action: 'login' },
  SESSION_EXPIRED: {
    message: 'La tua sessione è scaduta e i dati collegati sono stati cancellati. Puoi ricominciare quando vuoi.',
    action: 'login',
  },
  FORBIDDEN: { message: 'Non hai i permessi per questa pagina.', action: 'home' },
  NOT_FOUND: { message: 'Non abbiamo trovato quello che cercavi.', action: 'home' },
  REQUEST_REJECTED: { message: 'Operazione non consentita.', action: 'none' },
  RATE_LIMITED: { message: 'Troppe richieste in poco tempo: riprova tra qualche minuto.', action: 'retry' },

  // --- Upload of the export
  UPLOAD_FILE_MISSING: { message: 'Seleziona il file zip scaricato da Spotify.', action: 'none' },
  UPLOAD_USER_MISSING: { message: 'Per caricare l\'export inizia una sessione o accedi con Last.fm.', action: 'login' },
  UPLOAD_NOT_ZIP: { message: 'Il file deve essere uno .zip, così come lo invia Spotify. Non estrarlo prima di caricarlo.', action: 'none' },
  UPLOAD_TOO_LARGE: { message: 'Il file è troppo grande (massimo 200 MB).', action: 'export-guide' },
  UPLOAD_INVALID_ZIP: { message: 'Il file sembra danneggiato. Scaricalo di nuovo da Spotify e riprova.', action: 'export-guide' },
  UPLOAD_NO_STREAMING_FILES: {
    message: 'Questo zip non contiene la cronologia degli ascolti. Su Spotify richiedi la "Cronologia streaming estesa", non i dati dell\'account.',
    action: 'export-guide',
  },
  UPLOAD_UNREADABLE_FILES: { message: 'Non riusciamo a leggere i file della cronologia. Prova a scaricare di nuovo l\'export.', action: 'export-guide' },
  UPLOAD_PROCESSING_FAILED: { message: 'Non siamo riusciti a importare i tuoi ascolti. Riprova più tardi.', action: 'retry' },
  UPLOAD_NOT_FOUND: { message: 'Non troviamo questo caricamento.', action: 'upload' },

  // --- Last.fm account ('relink': log in with Last.fm again)
  LASTFM_NOT_LINKED: { message: 'Il tuo account Last.fm non è collegato.', action: 'relink' },
  LASTFM_AUTH_FAILED: { message: 'Il collegamento con Last.fm non è riuscito o non è più attivo. Ricollega l\'account.', action: 'relink' },
  LASTFM_UNAVAILABLE: { message: 'Last.fm non risponde in questo momento. Riproveremo più tardi.', action: 'retry' },
  LASTFM_RATE_LIMITED: { message: 'Last.fm è momentaneamente occupato: gli ascolti arriveranno con un po\' di ritardo.', action: 'none' },
  LASTFM_USER_PRIVATE: {
    message: 'I tuoi ascolti su Last.fm sono privati: rendili visibili nelle impostazioni di Last.fm, poi ricollega l\'account.',
    action: 'relink',
  },
  LASTFM_SPOTIFY_NOT_SCROBBLING: {
    message: 'Da qualche giorno non riceviamo ascolti nuovi da Last.fm. Se hai ascoltato musica su Spotify, controlla che Spotify sia ancora collegato a Last.fm.',
    action: 'none',
  },
  LASTFM_IMPORT_NOT_FOUND: { message: 'Non troviamo questa importazione.', action: 'none' },
  LASTFM_IMPORT_FAILED: { message: 'Non siamo riusciti a importare tutto lo storico di Last.fm. Riprova più tardi.', action: 'retry' },

  // --- City
  CITY_INVALID_RANGE: {
    message: "Controlla le date: l'inizio deve venire prima della fine e non può essere nel futuro.",
    action: 'none',
  },
  CITY_CATALOG_MISSING: { message: 'Stiamo ancora importando i tuoi ascolti. Potrai creare la tua città tra poco.', action: 'none' },
  CITY_BUILD_FAILED: { message: 'Non siamo riusciti a costruire la tua città. Riprova più tardi.', action: 'retry' },
  CITY_NOT_FOUND: { message: 'Questa città non esiste più.', action: 'home' },
  CITY_LIMIT_REACHED: { message: 'Hai già 10 città salvate: cancellane una per crearne una nuova.', action: 'none' },
  CITY_INVALID_LAYOUT: { message: 'Tipo di mappa non disponibile.', action: 'none' },
  CITY_INVALID_RECIPE: { message: 'Controlla i parametri della mappa.', action: 'none' },
  CITY_INVALID_NAME: { message: 'Il nome della città è troppo lungo.', action: 'none' },
  CITY_INVALID_GENRES: { message: 'Hai selezionato troppi generi.', action: 'none' },
  CITY_INVALID_DISTRICTS: { message: 'Controlla il numero di quartieri.', action: 'none' },
  MUSICBRAINZ_UNAVAILABLE: {
    message: 'Il catalogo dei generi musicali non è disponibile al momento. La città verrà costruita appena torna online.',
    action: 'none',
  },
  CITY_NO_PLAYS: { message: 'Nessun ascolto in questo periodo per i generi scelti.', action: 'none' },
  CITY_GENRES_UNAVAILABLE: { message: 'Non riusciamo ancora a riconoscere i generi dei tuoi artisti.', action: 'none' },
  CITY_LOW_COVERAGE: {
    message: 'La città mostra solo una parte dei tuoi ascolti: alcuni artisti non sono nel nostro catalogo.',
    action: 'none',
  },
  CITY_MANY_DISTRICTS: { message: 'Questa città ha molti quartieri: la mappa può risultare affollata.', action: 'none' },

  // --- Pipeline
  PIPELINE_RUN_NOT_FOUND: { message: 'Non troviamo questa elaborazione.', action: 'none' },
  PIPELINE_TIMEOUT: { message: 'La costruzione sta richiedendo troppo tempo. Riprova.', action: 'retry' },
  PROCESS_CANCELLED: { message: 'L\'operazione è stata annullata. Puoi riprovare.', action: 'retry' },

  // --- User data
  USER_DATA_DELETION_INCOMPLETE: {
    message: 'Non siamo riusciti a cancellare tutti i tuoi dati. Riprova tra poco: la cancellazione riprende da dove si è fermata.',
    action: 'retry',
  },

  // --- Administration (never shown to end users, mapped to the generic message)
  MB_RETRIEVE_ALREADY_RUNNING: { message: 'Qualcosa è andato storto. Riprova tra poco.', action: 'retry' },
  MB_TOO_MANY_ITEMS: { message: 'Qualcosa è andato storto. Riprova tra poco.', action: 'retry' },
  MB_GENRE_NOT_FOUND: { message: 'Qualcosa è andato storto. Riprova tra poco.', action: 'retry' },

  // --- Admin panel
  ADMIN_INVALID_CREDENTIALS: { message: 'Utente o password non corretti.', action: 'none' },
  PROCESS_NOT_CANCELLABLE: { message: 'Il processo non è in corso o è già terminato.', action: 'none' },
};

/** Message for a code; unknown codes fall back to INTERNAL_ERROR (never an empty screen). */
export function errorMessage(code: string | null | undefined): ErrorMessage {
  return (code && MESSAGES[code]) || MESSAGES['INTERNAL_ERROR'];
}

export const KNOWN_ERROR_CODES = Object.keys(MESSAGES);
