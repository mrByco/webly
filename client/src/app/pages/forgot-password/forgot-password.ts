import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);

  protected readonly routes = AppRoutes;
  protected readonly submitting = signal(false);
  protected readonly sent = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);

    try {
      await this.auth.forgotPassword(this.form.getRawValue().email);
    } catch {
      // Swallowed on purpose. The backend answers identically for a registered and an unregistered
      // address so this page cannot be used to find out who has an account; showing an error for a
      // rate limit or a network blip would undo that by making some attempts look different.
    } finally {
      this.sent.set(true);
      this.submitting.set(false);
    }
  }
}
