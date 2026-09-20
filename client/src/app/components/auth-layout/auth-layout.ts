import { Component, input } from '@angular/core';
import { Icon } from '../../shared/icon';

/**
 * The split screen shared by login and registration: a branded panel on one side, the form on the
 * other.
 *
 * The brand panel is decorative, so it is hidden below `lg` rather than stacked — on a phone it
 * would push the form below the fold, and the form is the only thing anyone came here to use. A
 * compact wordmark stands in for it there.
 */
@Component({
  selector: 'app-auth-layout',
  imports: [Icon],
  templateUrl: './auth-layout.html',
})
export class AuthLayout {
  readonly heading = input.required<string>();
  readonly subheading = input<string>('');

  protected readonly features = [
    'Describe what you want. Webly writes the page.',
    'Every change is a version you can bring back.',
    'Connect your domain and publish in one click.',
  ];
}
