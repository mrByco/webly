import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AppRoutes } from '../app.routes.paths';
import { AuthService } from '../services/auth.service';

/**
 * Keeps unauthenticated visitors out of a route, sending them to the login page with somewhere to
 * come back to.
 *
 * It waits for the first `/me` answer before deciding: a session restored from cookies is not known
 * to exist until the backend says so, and bouncing the user to login in the meantime would log them
 * out on every refresh.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.ensureLoaded();

  return auth.authenticated() ? true : router.parseUrl(AppRoutes.login.build(state.url));
};
