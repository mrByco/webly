import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Holds a signed-in visitor at the verification screen until their address is proven.
 *
 * The backend already refuses everything that matters to an unverified caller, so this is not the
 * security boundary — it is what stops the app showing pages whose every button would fail.
 *
 * It waits for the first `/me` answer itself rather than relying on `authGuard` having run: guards
 * on the same route start together, so a synchronous check here would read an empty profile, decide
 * nobody is signed in, and wave the request through. Both guards share the one in-flight request.
 *
 * Where an unverified visitor goes is `nextStop`'s to say, not this guard's.
 *
 * Google accounts arrive verified, so they never see this.
 */
export const verifiedGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.ensureLoaded();

  return auth.needsEmailVerification() ? router.parseUrl(auth.nextStop(state.url)) : true;
};
