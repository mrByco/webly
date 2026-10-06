import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AuthLayout } from '../../components/auth-layout/auth-layout';
import { AuthService } from '../../services/auth.service';
import { messageOf } from '../../models/problem-details';

@Component({
  selector: 'app-reset-password',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
})
export class ResetPasswordPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly routes = AppRoutes;
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The link itself was refused, which is the one failure a new link fixes — so the only one offered it. */
  protected readonly expired = signal(false);

  protected readonly token = this.route.snapshot.queryParamMap.get('token');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting() || !this.token) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    this.expired.set(false);

    try {
      await this.auth.resetPassword(this.token, this.form.getRawValue().newPassword);

      // `nextStop`, not home: choosing a new password signs the person in and proves their address, so this is
      // the same junction sign-in stands at — and the answer for somebody who has no site yet is the screen that
      // makes one, not the landing page they have just been handed the keys past. Home is where a site-less
      // account used to land, reading the marketing copy for a product it had already bought.
      await this.router.navigateByUrl(this.auth.nextStop());
    } catch (failure: unknown) {
      // Only a 400 is about the link. This used to say "not valid any more" for every failure, so with Webly
      // unreachable somebody holding a perfectly good link was sent for another, which would have failed the same way.
      this.expired.set((failure as { status?: unknown })?.status === 400);
      this.error.set(messageOf(failure, 'The new password could not be saved. Please try again.'));
    } finally {
      this.submitting.set(false);
    }
  }
}
