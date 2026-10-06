import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { OnboardingLayout } from '../../components/onboarding-layout/onboarding-layout';
import { AuthService } from '../../services/auth.service';
import { messageOf, notActedOn } from '../../models/problem-details';

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
    const code = value.replace(/\D/g, '').slice(0, 6);

    this.code.set(code);
    this.error.set(null);

    // The sixth digit is the whole of the decision, so it is what submits.
    //
    // A one-time code has a known length and exactly one thing that can happen next; every OTP field anybody
    // has used advances by itself, and this one made somebody type six digits and then go looking for a
    // button. It is the worst screen in the product to add a step to: the code is on a phone, the box is on a
    // laptop, and the moment they look up from one to the other is the moment they have to find the other.
    // The page's own last line already promises "this page moves on by itself" — of the link, which made the
    // code path inconsistent with the copy printed underneath it.
    //
    // Nothing else changes: `submitCode` returns early unless `canSubmit()`, so a sixth digit typed while a
    // request is in flight is a no-op, and a wrong code clears the box and says so exactly as it did.
    if (code.length === 6) void this.submitCode();
  }

  private async verifyLink(token: string): Promise<void> {
    this.state.set('checking');

    try {
      await this.auth.verifyEmail(token);
      await this.continue();
    } catch (failure: unknown) {
      // Find out who this is before rendering the failure. Without it a signed-in user opening an
      // expired link is told to sign in — which they already are — instead of being offered the
      // code box and the resend button that actually get them unstuck.
      await this.auth.refresh();
      this.state.set(this.auth.authenticated() ? 'prompt' : 'failed');
      this.error.set(
        refused(failure)
          ? 'That link is not valid any more. Type the code instead, or ask for a new one.'
          : messageOf(failure, 'That link could not be checked just now. Open it again in a moment.'),
      );
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
    } catch (failure: unknown) {
      this.state.set('prompt');

      // Only a refusal is about the code, so only a refusal clears it. With Webly unreachable this used to wipe a
      // correct code and call it wrong, and the person would check it against the email and find it right.
      if (refused(failure)) {
        this.code.set('');
        this.error.set('That code is wrong or has expired. Check it, or ask for a new one.');
      } else {
        this.error.set(messageOf(failure, 'That code could not be checked just now. Press Confirm to try again.'));
      }
    }
  }

  protected async resend(): Promise<void> {
    this.error.set(null);

    try {
      await this.auth.resendVerification();
      this.resent.set(true);
    } catch (failure: unknown) {
      // "On its way" either way, except when nothing was done at all: the request never arrived, or this connection
      // has asked for a lot of email recently. Whether a mail went out otherwise depends on the cooldown, and the
      // endpoint deliberately does not say. It used to claim a new code was coming in every case — including those
      // two, where somebody then waits for a code that is not coming.
      if (notActedOn(failure)) this.error.set(messageOf(failure));
      else this.resent.set(true);
    }
  }

  private async continue(): Promise<void> {
    await this.router.navigateByUrl(this.auth.nextStop(this.redirect));
  }

  protected async goToLogin(): Promise<void> {
    await this.router.navigateByUrl(AppRoutes.login.build(this.redirect ?? undefined));
  }
}

/** The answer was Webly's refusal of what was presented — the link or the code — rather than anything else going wrong. */
function refused(failure: unknown): boolean {
  return (failure as { status?: unknown })?.status === 400;
}
