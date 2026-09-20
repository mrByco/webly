import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Sends cookies with API calls.
 *
 * The auth tokens live in HttpOnly cookies, which script cannot read or attach by hand — the only
 * way they reach the backend is `withCredentials`. Scoped to our own API paths so a future call to
 * some third party never carries them.
 */
export const credentialsInterceptor: HttpInterceptorFn = (req, next) => {
  const isOwnApi = req.url.startsWith('/api/') || req.url.startsWith('/health');

  return next(isOwnApi ? req.clone({ withCredentials: true }) : req);
};
