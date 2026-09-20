import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AppRoutes } from '../../app.routes.paths';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly routes = AppRoutes;
  protected readonly googleEnabled = this.auth.googleEnabled;
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    // Matches the server's rule. Length only: composition rules push people towards shorter,
    // more guessable passwords, and there is no reset flow yet to rescue anyone who forgets.
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  constructor() {
    void this.auth.loadProviders();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    try {
      await this.auth.register(this.form.getRawValue());

      // Straight to the verification screen: a new local account is unverified by definition, and
      // `nextStop` is what knows that.
      await this.router.navigateByUrl(this.auth.nextStop(this.redirect()));
    } catch (failure: unknown) {
      const status = (failure as { status?: number })?.status;

      this.error.set(
        status === 409
          ? 'There is already an account with that email address. Sign in instead.'
          : 'That did not work. Please try again.',
      );
    } finally {
      this.submitting.set(false);
    }
  }

  protected continueWithGoogle(): void {
    // Google addresses arrive proven, so that route skips verification entirely.
    this.auth.startGoogle(this.redirect() ?? AppRoutes.home.build());
  }

  /** Set when registration was reached from an invitation, so the chain can return to it. */
  protected redirect(): string | null {
    return this.route.snapshot.queryParamMap.get('redirect');
  }
}
