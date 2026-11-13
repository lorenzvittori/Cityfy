import { HttpErrorResponse } from '@angular/common/http';

/** A problem of one field (ProblemDetails "problems"), e.g. a key of the map parameters of CITY_INVALID_RECIPE. */
export interface FieldProblem {
  key: string;
  code: string;
}

/** Error of an API call, normalized from the ProblemDetails body (code + traceId + problems). */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
    readonly traceId: string | null,
    readonly problems: FieldProblem[] = [],
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
      const problems = Array.isArray(body.problems)
        ? body.problems.filter((p: unknown): p is FieldProblem => typeof (p as FieldProblem)?.key === 'string' && typeof (p as FieldProblem)?.code === 'string')
        : [];
      return new ApiError(code, error.status, typeof body.traceId === 'string' ? body.traceId : null, problems);
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
