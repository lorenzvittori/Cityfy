import { HttpErrorResponse } from '@angular/common/http';
import { ApiError } from './api-error';
import { errorMessage } from './error-messages';

describe('ApiError.from', () => {
  it('reads code and traceId from the ProblemDetails body', () => {
    const e = ApiError.from(
      new HttpErrorResponse({ status: 404, error: { code: 'CITY_NOT_FOUND', traceId: 'abc' } }),
    );
    expect(e.code).toBe('CITY_NOT_FOUND');
    expect(e.traceId).toBe('abc');
  });

  it('maps a network failure (status 0) to SERVICE_UNAVAILABLE', () => {
    expect(ApiError.from(new HttpErrorResponse({ status: 0 })).code).toBe('SERVICE_UNAVAILABLE');
  });

  it('falls back on the status when there is no code', () => {
    expect(ApiError.from(new HttpErrorResponse({ status: 401 })).code).toBe('UNAUTHENTICATED');
    expect(ApiError.from(new HttpErrorResponse({ status: 500, error: 'boom' })).code).toBe('INTERNAL_ERROR');
  });
});

describe('errorMessage', () => {
  it('never returns an empty message, even for unknown codes', () => {
    expect(errorMessage('SOMETHING_NEW').message).toBe(errorMessage('INTERNAL_ERROR').message);
    expect(errorMessage(null).message.length).toBeGreaterThan(0);
  });

  it('offers to log in with Last.fm again when its reading stopped, and the guide for a wrong export', () => {
    expect(errorMessage('LASTFM_AUTH_FAILED').action).toBe('relink');
    expect(errorMessage('LASTFM_USER_PRIVATE').action).toBe('relink');
    expect(errorMessage('UPLOAD_NO_STREAMING_FILES').action).toBe('export-guide');
  });
});
