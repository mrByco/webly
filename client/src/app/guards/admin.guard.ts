import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { AppRoutes } from '../app.routes.paths';

/**
 * Keeps the administration screens to administrators. Not the boundary — the API answers 403 to anybody else —
 * but a screen whose only request is refused is a broken page, so a customer who types the address is simply
 * taken home. It waits for the profile for the reason `verifiedGuard` does.
 */
export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.ensureLoaded();

  return auth.isAdmin() ? true : router.parseUrl(AppRoutes.home.build());
};
