import { Component, computed, input } from '@angular/core';

/**
 * The initial-in-a-circle placeholder that stands in for someone's face. One component because the
 * header and the member list draw the same thing, and a profile picture — the field is already on
 * the API — should start being shown in one place rather than two.
 */
@Component({
  selector: 'app-avatar',
  templateUrl: './avatar.html',
})
export class Avatar {
  readonly name = input<string | null | undefined>(null);
  readonly title = input<string | null | undefined>(null);

  /**
   * Tailwind sizing utilities for the circle, so a caller asks for a size rather than reaching
   * through this component's markup with a child selector.
   */
  readonly size = input('size-9 text-sm');

  protected readonly initial = computed(() => (this.name() ?? '?').trim().charAt(0).toUpperCase());
}
