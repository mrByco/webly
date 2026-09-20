import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { inject } from '@angular/core';

/**
 * The app's icons, drawn inline.
 *
 * Inline rather than fetched: one network request per glyph is a slow first paint, an `<img>` cannot
 * take `currentColor` (the usual workaround is recolouring with `filter: invert()` hue rotations), and
 * a third-party host is a dependency on somebody else's uptime for the shape of a button.
 *
 * The set is deliberately small and closed. Shapes are Lucide's (ISC) where Lucide has them,
 * transcribed at the same 24-unit grid and 2-unit stroke so they sit together.
 *
 * Note this is the *app's* icon set. A published site's feature icons are a separate, also-closed set
 * on the backend (`Icons` in the renderer) — they have to be inlined into the rendered HTML, where
 * this component does not reach.
 *
 * Sized with the usual utilities: `<app-icon name="globe" class="size-5" />`.
 */
type IconName =
  | 'arrow-right'
  | 'check'
  | 'chevron-left'
  | 'chevron-right'
  | 'circle-alert'
  | 'clock'
  | 'copy'
  | 'external'
  | 'eye'
  | 'globe'
  | 'layout'
  | 'link'
  | 'log-out'
  | 'pencil'
  | 'plus'
  | 'restore'
  | 'rocket'
  | 'send'
  | 'settings'
  | 'sparkles'
  | 'square'
  | 'trash'
  | 'user'
  | 'x';

const PATHS: Record<IconName, string> = {
  'arrow-right': '<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
  'chevron-left': '<path d="m15 18-6-6 6-6"/>',
  'chevron-right': '<path d="m9 18 6-6-6-6"/>',
  'circle-alert': '<circle cx="12" cy="12" r="10"/><path d="M12 8v4"/><path d="M12 16h.01"/>',
  clock: '<circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>',
  copy:
    '<rect width="14" height="14" x="8" y="8" rx="2"/>'
    + '<path d="M4 16a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2"/>',
  external:
    '<path d="M15 3h6v6"/><path d="M10 14 21 3"/>'
    + '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
  eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/>',
  globe:
    '<circle cx="12" cy="12" r="10"/><path d="M2 12h20"/>'
    + '<path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"/>',
  layout: '<rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 9h18"/><path d="M9 21V9"/>',
  link:
    '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/>'
    + '<path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
  'log-out': '<path d="m16 17 5-5-5-5"/><path d="M21 12H9"/><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>',
  pencil: '<path d="M17 3a2.85 2.85 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"/><path d="m15 5 4 4"/>',
  plus: '<path d="M5 12h14"/><path d="M12 5v14"/>',
  restore: '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/>',
  rocket:
    '<path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91 0z"/>'
    + '<path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z"/>'
    + '<path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0"/>'
    + '<path d="M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5"/>',
  send: '<path d="M22 2 11 13"/><path d="m22 2-7 20-4-9-9-4z"/>',
  settings:
    '<circle cx="12" cy="12" r="3"/>'
    + '<path d="M12 2v3"/><path d="M12 19v3"/><path d="M4.9 4.9l2.1 2.1"/><path d="M17 17l2.1 2.1"/>'
    + '<path d="M2 12h3"/><path d="M19 12h3"/><path d="M4.9 19.1 7 17"/><path d="M17 7l2.1-2.1"/>',
  sparkles:
    '<path d="m12 3 1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/>'
    + '<path d="M18 16.5 18.8 18.7 21 19.5 18.8 20.3 18 22.5 17.2 20.3 15 19.5 17.2 18.7z"/>',
  square: '<rect width="12" height="12" x="6" y="6" rx="2"/>',
  trash: '<path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
  user: '<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  x: '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
};

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-block shrink-0 size-5', 'aria-hidden': 'true' },
  template: `
    <svg
      class="size-full"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      [innerHTML]="body()"></svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();

  private readonly sanitizer = inject(DomSanitizer);

  /**
   * Trusted because the markup is the constant above, never anything a user typed — `name` is a union
   * of literals and an unknown key draws nothing rather than falling back to a string.
   */
  protected readonly body = computed<SafeHtml>(() =>
    this.sanitizer.bypassSecurityTrustHtml(PATHS[this.name()] ?? ''),
  );
}

export type { IconName };
