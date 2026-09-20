import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { OnboardingLayout } from '../../components/onboarding-layout/onboarding-layout';
import { AuthService } from '../../services/auth.service';

type State = 'prompt' | 'checking' | 'failed';

/**
 * Where onboarding waits for the address to be proven — by the six digits from the email, or by the
 * link, which lands here with a token and verifies on arrival.
 *
 * The token is spent by a POST from this page rather than by the link itself hitting the API.
 * Corporate mail scanners follow URLs in incoming email, and a single-use token consumed by a
 * scanner before the user ever clicks is an unexplainable "link already used" complaint.
 */
@Component({
  selector: 'app-verify-email',
  imports: [FormsModule, OnboardingLayout],
  templateUrl: './verify-email.html',
})
export class VerifyEmailPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly me = this.auth.me;

  protected readonly state = signal<State>('prompt');
  protected readonly code = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly resent = signal(false);

  protected readonly canSubmit = computed(
    () => this.code().length === 6 && this.state() !== 'checking',
  );

  /**
   * Where onboarding continues. The query parameter is set when the guard or a page sent them here;
   * the remembered invitation covers the other route in — clicking the link straight out of the
   * inbox, which arrives with no memory of what they were doing.
   */
  private readonly redirect =
    this.route.snapshot.queryParamMap.get('redirect');

  constructor() {
    const token = this.route.snapshot.queryParamMap.get('token');

    if (token) {
      void this.verifyLink(token);
    }
  }

  /** Digits only, capped at six — a pasted code often arrives with spaces around it. */
  protected onCodeInput(value: string): void {
    this.code.set(value.replace(/\D/g, '').slice(0, 6));
    this.error.set(null);
  }

  private async verifyLink(token: string): Promise<void> {
    this.state.set('checking');

    try {
      await this.auth.verifyEmail(token);
      await this.continue();
    } catch {
      // Find out who this is before rendering the failure. Without it a signed-in user opening an
      // expired link is told to sign in — which they already are — instead of being offered the
      // code box and the resend button that actually get them unstuck.
      await this.auth.refresh();
      this.state.set(this.auth.authenticated() ? 'prompt' : 'failed');
      this.error.set('That link is not valid any more. Type the code instead, or ask for a new one.');
    }
  }

  protected async submitCode(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.state.set('checking');
    this.error.set(null);

    try {
      await this.auth.verifyEmailCode(this.code());
      await this.continue();
    } catch {
      this.state.set('prompt');
      this.code.set('');
      this.error.set('That code is wrong or has expired. Check it, or ask for a new one.');
    }
  }

  protected async resend(): Promise<void> {
    this.error.set(null);

    try {
      await this.auth.resendVerification();
    } finally {
      // Shown either way: whether a mail actually went out depends on the cooldown, and saying so
      // would leak more than it helps.
      this.resent.set(true);
    }
  }

  private async continue(): Promise<void> {
    await this.router.navigateByUrl(this.auth.nextStop(this.redirect));
  }

  protected async goToLogin(): Promise<void> {
    await this.router.navigateByUrl(AppRoutes.login.build(this.redirect ?? undefined));
  }
}
