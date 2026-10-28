import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Turns an RFC 9457 ProblemDetails response (what the services return) into a readable message. */
export function problemMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong.';
  if (error.status === 0) return 'The API gateway is unreachable. Is the backend running?';

  const problem = error.error as ProblemDetails | null;
  const firstValidationError = problem?.errors && Object.values(problem.errors).flat()[0];

  return firstValidationError ?? problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}
