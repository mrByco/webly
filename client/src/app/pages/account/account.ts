import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-account',
  imports: [AppShell, ReactiveFormsModule],
  templateUrl: './account.html',
})
export class AccountPage {
  private readonly auth = inject(AuthService);

  protected readonly routes = AppRoutes;
  protected readonly me = this.auth.me;
  protected readonly submitting = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);

  /**
   * A Google-created account has no password to confirm, so the form asks for one less thing and
   * reads as "set a password" rather than "change" it.
   */
  protected readonly settingFirstPassword = computed(() => !this.me().hasPassword);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: [''],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    this.saved.set(false);

    const { currentPassword, newPassword } = this.form.getRawValue();

    try {
      await this.auth.changePassword(
        newPassword,
        this.settingFirstPassword() ? undefined : currentPassword,
      );

      this.form.reset();
      this.saved.set(true);
    } catch {
      this.error.set(
        this.settingFirstPassword()
          ? 'That password could not be set. You may need to confirm your email address first.'
          : 'That is not your current password.',
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
