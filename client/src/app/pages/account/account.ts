import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { Modal } from '../../components/modal/modal';
import { AuthService } from '../../services/auth.service';
import { messageOf } from '../../models/problem-details';

@Component({
  selector: 'app-account',
  imports: [AppShell, Modal, ReactiveFormsModule],
  templateUrl: './account.html',
})
export class AccountPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

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

  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deletePassword = signal('');
  protected readonly deleteError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: [''],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  protected cancelDelete(): void {
    this.confirmingDelete.set(false);
    this.deletePassword.set('');
    this.deleteError.set(null);
  }

  /**
   * Closes the account and leaves. Navigating to the sign-in screen rather than the app's own home, because
   * there is nobody to show it to any more — and the guard would send them here anyway, which is the same
   * destination arrived at by a bounce.
   */
  protected async deleteAccount(): Promise<void> {
    if (this.deleting()) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    try {
      await this.auth.deleteAccount(this.settingFirstPassword() ? undefined : this.deletePassword());

      await this.router.navigateByUrl(AppRoutes.login.build());
    } catch (failure) {
      this.deleteError.set(messageOf(failure, 'That account could not be closed.'));
    } finally {
      this.deleting.set(false);
    }
  }

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
