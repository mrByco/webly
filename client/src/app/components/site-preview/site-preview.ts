import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';

/**
 * The preview: an iframe over the site's own `next dev`, proxied through this origin.
 *
 * <b>It is the real dev server, not a re-render.</b> The same process the agent's edits land in, so what
 * somebody approves here is what the next build produces — and hot reload means an edit appears without
 * anything on this side asking for it. `PreviewController` is the proxy and explains why pointing a
 * browser at a sandbox is safe; the sandbox's address never reaches the browser.
 *
 * Two consequences shape this component:
 *
 * - <b>A preview can be cold.</b> A workspace takes tens of seconds to start and only a turn starts one,
 *   so when `ready` is false this shows a sentence instead of an iframe. An iframe pointed at a 503 shows
 *   the browser's own error page, which reads as the product being broken.
 * - <b>`reloadKey` is a fallback, not the mechanism.</b> Hot reload already updates the frame; the key is
 *   for a commit or a restore, where the whole tree changed underneath it. It is a counter rather than a
 *   timestamp so unrelated change detection does not make the frame flicker.
 */
@Component({
  selector: 'app-site-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './site-preview.html',
})
export class SitePreview {
  readonly siteNanoid = input.required<string>();

  /** Whether a workspace is warm. False shows the cold state rather than a frame that cannot load. */
  readonly ready = input<boolean>(false);

  /** What the workspace is doing while it starts, straight from the run's events. */
  readonly progress = input<string | undefined>(undefined);

  /** Changing this re-fetches the iframe. See the class comment. */
  readonly reloadKey = input<number>(0);

  private readonly sites = inject(SiteService);
  private readonly sanitizer = inject(DomSanitizer);

  /** Phone, tablet or desktop width, so somebody can check the thing they are about to publish. */
  protected readonly width = signal<'phone' | 'tablet' | 'full'>('full');

  protected readonly url = computed<SafeResourceUrl>(() => {
    const url = this.sites.previewUrl(this.siteNanoid());

    return this.sanitizer.bypassSecurityTrustResourceUrl(`${url}?r=${this.reloadKey()}`);
  });

  protected setWidth(width: 'phone' | 'tablet' | 'full'): void {
    this.width.set(width);
  }
}
