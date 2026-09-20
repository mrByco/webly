import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AppRoutes } from '../../app.routes.paths';
import { AuthService } from '../../services/auth.service';

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

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  constructor() {
    void this.auth.loadProviders();

    // The Google callback cannot render a page, so it reports failures by redirecting here.
    const failure = this.route.snapshot.queryParamMap.get('error');

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
    } catch {
      this.error.set('That email address and password do not match.');
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
