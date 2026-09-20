import { RenderMode, ServerRoute } from '@angular/ssr';
import { AppRoutes } from './app.routes.paths';

/**
 * Anything that depends on who is signed in is rendered in the browser.
 *
 * Auth lives in HttpOnly cookies, and the server would have to forward them to the backend — with
 * an absolute URL and the local dev certificate — to know anything about the visitor. That is three
 * problems for no benefit: a personalized page has nothing worth prerendering. The public pages
 * still get the full SSR treatment.
 *
 * The verify-email and reset-password pages are public in the sense that they need no session, but
 * they are Client-rendered too: both act on a token from the query string the moment they load, and
 * prerendering a page whose entire job is a side effect gains nothing.
 */
export const serverRoutes: ServerRoute[] = [
  { path: AppRoutes.login.path, renderMode: RenderMode.Prerender },
  { path: AppRoutes.register.path, renderMode: RenderMode.Prerender },
  { path: AppRoutes.forgotPassword.path, renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Client },
];
