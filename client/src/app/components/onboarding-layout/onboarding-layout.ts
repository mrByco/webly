import { Component, inject, input, output } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { Icon } from '../../shared/icon';

/**
 * The full-screen shell for onboarding: proving the address, then creating the first site.
 *
 * Deliberately without the app header. These screens are a sequence with one thing to do at a time,
 * and a nav bar offering three ways out of it is an invitation to leave halfway through — worse, on
 * the verification screen every one of those links leads somewhere the guard bounces you back from.
 *
 * Losing the header means losing the only way to sign out, so the footer keeps one: whoever is stuck
 * on the wrong account has to be able to leave it.
 */
@Component({
  selector: 'app-onboarding-layout',
  imports: [Icon],
  templateUrl: './onboarding-layout.html',
})
export class OnboardingLayout {
  private readonly auth = inject(AuthService);

  readonly heading = input.required<string>();
  readonly subheading = input<string>('');

  /** Shown top-right when the step can be abandoned. Emits instead of navigating itself. */
  readonly exitLabel = input<string | null>(null);
  readonly exit = output<void>();

  protected readonly me = this.auth.me;

  protected signOut(): Promise<void> {
    return this.auth.signOutToLogin();
  }
}
