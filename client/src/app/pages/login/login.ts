import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AppRoutes } from '../../app.routes.paths';
import { AuthService } from '../../services/auth.service';
import { messageOf } from '../../models/problem-details';

@Component({
  selector: 'app-login',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly routes = AppRoutes;
  protected readonly googleEnabled = this.auth.googleEnabled;
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  constructor() {
    void this.auth.loadProviders();

    // The Google callback cannot render a page, so it reports failures by redirecting here.
    const failure = this.route.snapshot.queryParamMap.get('error');

    // Sent here by `AuthService.sessionEnded`: the session finished on another device, and the page they were on
    // is where signing in returns them to.
    if (this.route.snapshot.queryParamMap.get('ended')) {
      this.notice.set('You have been signed out — your password was changed or your session ended elsewhere. Sign in again to carry on.');
    }

    if (failure === 'google_email_unverified') {
      this.error.set('That Google account has an unconfirmed email address, so it cannot be used to sign in.');
    } else if (failure) {
      this.error.set('Signing in with Google did not work. Please try again.');
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.form.getRawValue());
      await this.router.navigateByUrl(this.auth.nextStop(this.redirect()));
    } catch (failure: unknown) {
      // The refusal carries its own sentence — "That email address and password do not match." Nothing else is about
      // the password, and this used to say that sentence for all of it: with Webly unreachable, a sign-in told
      // somebody their password was wrong, which sends them off to reset one that was right.
      this.error.set(messageOf(failure, 'Signing in did not work. Please try again.'));
    } finally {
      this.submitting.set(false);
    }
  }

  protected continueWithGoogle(): void {
    this.auth.startGoogle(this.redirect() ?? AppRoutes.home.build());
  }

  private redirect(): string | null {
    return this.route.snapshot.queryParamMap.get('redirect');
  }
}
