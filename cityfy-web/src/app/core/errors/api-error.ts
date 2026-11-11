import { HttpErrorResponse } from '@angular/common/http';

/** Error of an API call, normalized from the ProblemDetails body (code + traceId). */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
    readonly traceId: string | null,
  ) {
    super(code);
  }

  static from(error: unknown): ApiError {
    if (error instanceof ApiError) return error;

    if (error instanceof HttpErrorResponse) {
      // status 0: network error, the API is not reachable at all
      if (error.status === 0) return new ApiError('SERVICE_UNAVAILABLE', 0, null);

      const body = typeof error.error === 'object' && error.error !== null ? error.error : {};
      const code = typeof body.code === 'string' ? body.code : fallbackCode(error.status);
      return new ApiError(code, error.status, typeof body.traceId === 'string' ? body.traceId : null);
    }

    return new ApiError('INTERNAL_ERROR', 0, null);
  }
}

function fallbackCode(status: number): string {
  if (status === 401) return 'UNAUTHENTICATED';
  if (status === 404) return 'NOT_FOUND';
  if (status === 503 || status === 502 || status === 504) return 'SERVICE_UNAVAILABLE';
  return status >= 500 ? 'INTERNAL_ERROR' : 'REQUEST_REJECTED';
}
