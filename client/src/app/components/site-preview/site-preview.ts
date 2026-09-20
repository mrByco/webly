import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';

/**
 * The preview: an iframe over the backend's renderer.
 *
 * <b>There is no client-side renderer, deliberately.</b> The pane loads the same output publishing
 * uploads, from the same code path (`PreviewSite` → `HtmlSiteRenderer`), so what somebody approves here
 * is byte-for-byte what goes live. A TypeScript viewer beside the C# renderer would be two descriptions
 * of what a site looks like, and the one that is wrong is always the one the customer saw.
 *
 * `reloadKey` is how a committed version gets onto the screen: bumping it changes the iframe's URL, which
 * is the only way to make a browser re-fetch a document it thinks it already has.
 */
@Component({
  selector: 'app-site-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './site-preview.html',
})
export class SitePreview {
  readonly siteNanoid = input.required<string>();

  /** Which page. The editor's page selector writes it; the default is the home page. */
  readonly page = input<string>('/');

  /** A version to look at instead of the draft — how the history previews the past. */
  readonly version = input<string | undefined>(undefined);

  /** Changing this re-fetches the iframe. See the class comment. */
  readonly reloadKey = input<number>(0);

  private readonly sites = inject(SiteService);
  private readonly sanitizer = inject(DomSanitizer);

  /** Phone, tablet or desktop width, so somebody can check the thing they are about to publish. */
  protected readonly width = signal<'phone' | 'tablet' | 'full'>('full');

  protected readonly url = computed<SafeResourceUrl>(() => {
    const url = this.sites.previewUrl(this.siteNanoid(), this.page(), this.version());

    // The cache-buster is the reload key rather than a timestamp: a timestamp would make every change
    // detection pass a new URL, and the iframe would flicker on unrelated renders.
    return this.sanitizer.bypassSecurityTrustResourceUrl(`${url}&r=${this.reloadKey()}`);
  });

  protected setWidth(width: 'phone' | 'tablet' | 'full'): void {
    this.width.set(width);
  }
}
