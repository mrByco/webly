import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { RealtimeService } from './realtime.service';
import { AppRoutes } from '../app.routes.paths';
import { Api } from '../api/api';
import { apiAuthAccountDelete } from '../api/fn/auth/api-auth-account-delete';
import { apiAuthLoginPost$Json } from '../api/fn/auth/api-auth-login-post-json';
import { apiAuthLogoutPost } from '../api/fn/auth/api-auth-logout-post';
import { apiAuthMeGet$Json } from '../api/fn/auth/api-auth-me-get-json';
import { apiAuthProvidersGet$Json } from '../api/fn/auth/api-auth-providers-get-json';
import { apiAuthRegisterPost$Json } from '../api/fn/auth/api-auth-register-post-json';
import { apiAuthEmailResendPost } from '../api/fn/email-verification/api-auth-email-resend-post';
import { apiAuthEmailVerifyCodePost$Json } from '../api/fn/email-verification/api-auth-email-verify-code-post-json';
import { apiAuthEmailVerifyPost$Json } from '../api/fn/email-verification/api-auth-email-verify-post-json';
import { apiAuthPasswordChangePost$Json } from '../api/fn/password/api-auth-password-change-post-json';
import { apiAuthPasswordForgotPost } from '../api/fn/password/api-auth-password-forgot-post';
import { apiAuthPasswordResetPost$Json } from '../api/fn/password/api-auth-password-reset-post-json';
import { LoginRequest } from '../api/models/login-request';
import { MeResponse } from '../api/models/me-response';
import { RegisterRequest } from '../api/models/register-request';

const ANONYMOUS: MeResponse = { isAuthenticated: false };

/**
 * Who is signed in, as far as the browser can tell.
 *
 * There is no token handling here and there cannot be: the access and refresh tokens are HttpOnly
 * cookies the browser attaches on its own, and refreshing them happens server-side inside the
 * request. So this service only ever asks the backend "who am I" and remembers the answer.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /**
   * Injected for one call: the sign-out has to take the hub connection with it. The dependency goes this way
   * round and must stay that way — the realtime service knows nothing about who is signed in, which is what
   * keeps it usable from the prerender (where there is no session) without a circle.
   */
  private readonly realtime = inject(RealtimeService);

  /**
   * Every call here is a no-op on the server. The tokens are cookies the render process cannot see
   * without being handed them, and an unanswerable HTTP request during prerendering does not fail —
   * it hangs until the build times out. So the server renders the signed-out shell and the browser
   * establishes who is actually here.
   */
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  /** False until the first `/me` answer arrives, so the UI can avoid deciding too early. */
  readonly loaded = signal(false);
  readonly me = signal<MeResponse>(ANONYMOUS);
  readonly googleEnabled = signal(false);

  readonly authenticated = computed(() => this.me().isAuthenticated);

  /**
   * Whether this user administers the platform. Read off the roles the profile already carries — the
   * backend folds it in there, so there is nothing extra to ask for.
   */
  readonly isAdmin = computed(() => this.me().roles?.includes('admin') ?? false);

  /** A signed-in user with no site yet — what the first-site screen keys off. */
  readonly needsSite = computed(() => this.authenticated() && !this.me().hasSite);

  /** Signed in but the address is unproven. Nothing past the verification screen is reachable. */
  readonly needsEmailVerification = computed(() => this.authenticated() && !this.me().emailVerified);

  /**
   * Where the app sends someone the moment it knows who they are — the one place onboarding order
   * is written down. Sign-in, registration, verification and the guard all ask this, so the chain
   * cannot say one thing on one path and something else on another.
   *
   * Proving the address comes first and has no way past it; then a site, because an account without one
   * has nothing to edit. A `redirect` outranks the first-site screen: the visitor was already on their
   * way somewhere, and sending them to onboarding instead is how a shared link loses its destination.
   */
  nextStop(redirect?: string | null): string {
    const me = this.me();

    if (!me.isAuthenticated) {
      return AppRoutes.login.build(redirect ?? undefined);
    }

    if (!me.emailVerified) {
      return AppRoutes.verifyEmail.build(redirect ?? undefined);
    }

    if (redirect) {
      return redirect;
    }

    return me.hasSite ? AppRoutes.home.build() : AppRoutes.newSite.build();
  }

  /**
   * Records a profile another call already got back, rather than re-asking `/me` for an answer we are
   * already holding.
   */
  setMe(me: MeResponse): void {
    this.me.set(me);
    this.loaded.set(true);
  }

  /**
   * Folds a fact another call already established into the held profile. Cheaper and more truthful
   * than re-asking `/me` for an answer that is one field different from the one in hand.
   */
  patchMe(changes: Partial<MeResponse>): void {
    this.me.update(me => ({ ...me, ...changes }));
  }

  /**
   * The first `/me` answer, asked for once however many callers want it. Guards on one route start
   * together and each would otherwise fire its own request for the same answer, so the in-flight
   * promise is shared until it settles.
   */
  async ensureLoaded(): Promise<MeResponse> {
    if (this.loaded()) {
      return this.me();
    }

    this.loading ??= this.refresh().finally(() => (this.loading = undefined));

    return this.loading;
  }

  private loading?: Promise<MeResponse>;

  async refresh(): Promise<MeResponse> {
    if (!this.isBrowser) {
      return ANONYMOUS;
    }

    try {
      this.me.set(await this.api.invoke(apiAuthMeGet$Json));
    } catch {
      // /me answers 200 even for a signed-out caller, so a failure here is the network or a
      // backend that is down — neither of which makes the user authenticated.
      this.me.set(ANONYMOUS);
    } finally {
      this.loaded.set(true);
    }

    return this.me();
  }

  async loadProviders(): Promise<void> {
    if (!this.isBrowser) {
      return;
    }

    try {
      const providers = await this.api.invoke(apiAuthProvidersGet$Json);
      this.googleEnabled.set(providers.google);
    } catch {
      this.googleEnabled.set(false);
    }
  }

  async login(body: LoginRequest): Promise<void> {
    this.me.set(await this.api.invoke(apiAuthLoginPost$Json, { body }));
    this.loaded.set(true);
  }

  async register(body: RegisterRequest): Promise<void> {
    this.me.set(await this.api.invoke(apiAuthRegisterPost$Json, { body }));
    this.loaded.set(true);
  }

  /**
   * Ends the session — including the hub connection, which is the part that used to be left behind.
   *
   * A hub reads its caller's identity once, during the handshake, so a socket opened while signed in stays that
   * person's for as long as it is open: after this call `/api/sites` answers 401 and, without the disconnect,
   * `StartChat` over that same connection still started a turn on their site. The realtime service is asked
   * first, because the point is that nothing is left connected as somebody who has signed out; the HTTP call
   * still runs if it fails, since a sign-out must not be blocked by a socket that will not close.
   */
  async logout(): Promise<void> {
    try {
      await this.realtime.disconnect();
    } finally {
      try {
        await this.api.invoke(apiAuthLogoutPost);
      } finally {
        this.me.set(ANONYMOUS);
      }
    }
  }

  /**
   * Ends the session and lands on login. Where sign-out goes is decided here rather than in each
   * component that offers it — the header and the onboarding footer must not disagree.
   */
  async signOutToLogin(): Promise<void> {
    await this.logout();
    await this.router.navigateByUrl(AppRoutes.login.build());
  }

  async verifyEmail(token: string): Promise<void> {
    // Returns the caller's profile and sets fresh cookies, so the browser that opened the link is
    // signed in and verified from here on.
    this.me.set(await this.api.invoke(apiAuthEmailVerifyPost$Json, { body: { token } }));
    this.loaded.set(true);
  }

  /** The six digits from the email. Signed-in callers only, which is why no token travels here. */
  async verifyEmailCode(code: string): Promise<void> {
    this.setMe(await this.api.invoke(apiAuthEmailVerifyCodePost$Json, { body: { code } }));
  }

  async resendVerification(): Promise<void> {
    await this.api.invoke(apiAuthEmailResendPost);
  }

  async forgotPassword(email: string): Promise<void> {
    await this.api.invoke(apiAuthPasswordForgotPost, { body: { email } });
  }

  async resetPassword(token: string, newPassword: string): Promise<void> {
    this.me.set(await this.api.invoke(apiAuthPasswordResetPost$Json, { body: { token, newPassword } }));
    this.loaded.set(true);
  }

  /**
   * Closes the account, which takes the sites, their history and their hosting with it. The session is gone
   * when this returns — the server clears the cookies and the row they name no longer exists — so the caller
   * navigates to the way back in rather than refreshing a profile that has nobody to describe.
   */
  async deleteAccount(currentPassword?: string): Promise<void> {
    await this.api.invoke(apiAuthAccountDelete, { body: { currentPassword } });

    this.me.set(ANONYMOUS);
    this.loaded.set(true);
  }

  async changePassword(newPassword: string, currentPassword?: string): Promise<void> {
    this.me.set(
      await this.api.invoke(apiAuthPasswordChangePost$Json, { body: { newPassword, currentPassword } }),
    );
    this.loaded.set(true);
  }

  /**
   * A full page navigation, not an XHR: this is an OAuth redirect to Google and back, which fetch
   * cannot follow.
   */
  startGoogle(returnUrl: string): void {
    window.location.href = `/api/auth/external/google/start?returnUrl=${encodeURIComponent(returnUrl)}`;
  }
}
