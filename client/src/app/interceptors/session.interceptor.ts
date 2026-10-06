import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Sends somebody whose session has ended to sign in again, from wherever they were.
 *
 * A session can end without this browser doing anything: a password changed or reset on another device ends every
 * other session at once. An editor left open then answered each next action with the wrong sentence — "That could not
 * be saved." on opening History, "Webly cannot be reached" on sending a message — and only a reload showed the sign-in
 * page. A 401 from the app's API means exactly that, so it is answered once, here; the error still reaches its caller.
 * Not for `/api/auth/`, whose 401s are about the credentials just typed, and which is where signing in happens.
 */
export const sessionInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  return next(req).pipe(
    catchError((failure: unknown) => {
      const ended = failure instanceof HttpErrorResponse
        && failure.status === 401
        && req.url.startsWith('/api/')
        && !req.url.startsWith('/api/auth/');

      if (ended) void auth.sessionEnded();

      return throwError(() => failure);
    }),
  );
};
