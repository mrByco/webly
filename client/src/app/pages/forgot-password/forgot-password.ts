import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AuthService } from '../../services/auth.service';
import { messageOf, notActedOn } from '../../models/problem-details';

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
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    try {
      await this.auth.forgotPassword(this.form.getRawValue().email);
      this.sent.set(true);
    } catch (failure: unknown) {
      // The backend answers identically for a registered and an unregistered address, so this page cannot be used
      // to find out who has an account — and a failure Webly could only produce after looking the address up stays
      // hidden behind the same "on its way" for that reason. Two are said, because neither looked: the request
      // never arrived, or this connection has asked for a lot of email (counted per connection, before the
      // endpoint runs). Both used to be swallowed too, which told somebody a link was on its way when nothing had
      // been sent, and left them waiting for it.
      if (notActedOn(failure)) this.error.set(messageOf(failure));
      else this.sent.set(true);
    } finally {
      this.submitting.set(false);
    }
  }
}
