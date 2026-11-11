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
  UNAUTHENTICATED: { message: 'Accedi con Spotify per vedere la tua città.', action: 'login' },
  FORBIDDEN: { message: 'Non hai i permessi per questa pagina.', action: 'home' },
  NOT_FOUND: { message: 'Non abbiamo trovato quello che cercavi.', action: 'home' },
  REQUEST_REJECTED: { message: 'Operazione non consentita.', action: 'none' },

  // --- Upload of the export
  UPLOAD_FILE_MISSING: { message: 'Seleziona il file zip scaricato da Spotify.', action: 'none' },
  UPLOAD_USER_MISSING: { message: 'Collega prima il tuo account Spotify.', action: 'login' },
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

  // --- Spotify account
  SPOTIFY_AUTH_DENIED: { message: 'Senza il permesso di Spotify non possiamo leggere i tuoi ascolti recenti.', action: 'login' },
  SPOTIFY_LOGIN_EXPIRED: { message: 'Il collegamento è scaduto. Riprova.', action: 'login' },
  SPOTIFY_TOKEN_EXCHANGE_FAILED: { message: 'Spotify non ha completato il collegamento. Riprova tra poco.', action: 'login' },
  SPOTIFY_ACCOUNT_NOT_LINKED: { message: 'Il tuo account Spotify non è collegato.', action: 'login' },
  SPOTIFY_REAUTH_REQUIRED: {
    message: 'Il collegamento con Spotify non è più attivo. Ricollega l\'account per continuare ad aggiornare la città.',
    action: 'relink',
  },
  SPOTIFY_RATE_LIMITED: { message: 'Spotify è momentaneamente occupato. Riproveremo in automatico.', action: 'none' },
  SPOTIFY_UNAVAILABLE: { message: 'Spotify non risponde in questo momento. Riproveremo più tardi.', action: 'none' },
  SPOTIFY_RETRIEVE_FAILED: { message: 'Non siamo riusciti ad aggiornare i tuoi ascolti.', action: 'retry' },
  RETRIEVE_JOB_NOT_FOUND: { message: 'Non troviamo questo aggiornamento.', action: 'none' },

  // --- City
  CITY_INVALID_PERIOD: { message: 'Periodo non valido.', action: 'home' },
  CITY_NOT_READY: { message: 'La tua città non è ancora pronta.', action: 'none' },
  CITY_BUILD_FAILED: { message: 'Non siamo riusciti a costruire la tua città. Riprova più tardi.', action: 'retry' },
  MUSICBRAINZ_UNAVAILABLE: {
    message: 'Il catalogo dei generi musicali non è disponibile al momento. La città verrà costruita appena torna online.',
    action: 'none',
  },
  CITY_NO_PLAYS: { message: 'Nessun ascolto in questo periodo.', action: 'none' },
  CITY_GENRES_UNAVAILABLE: { message: 'Non riusciamo ancora a riconoscere i generi dei tuoi artisti.', action: 'none' },
  CITY_LOW_COVERAGE: {
    message: 'La città mostra solo una parte dei tuoi ascolti: alcuni artisti non sono nel nostro catalogo.',
    action: 'none',
  },

  // --- Pipeline
  PIPELINE_RUN_NOT_FOUND: { message: 'Non troviamo questa elaborazione.', action: 'none' },
  PIPELINE_TIMEOUT: { message: 'La costruzione sta richiedendo troppo tempo. Riprova.', action: 'retry' },
  PROCESS_CANCELLED: { message: 'L\'operazione è stata annullata. Puoi riprovare.', action: 'retry' },

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
