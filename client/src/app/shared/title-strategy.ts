import { Injectable, effect, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { SiteService } from '../services/site.service';

/**
 * What the browser tab says: the screen, then the site when there is one, then Webly — "History · Ridgeway Cycles ·
 * Webly".
 *
 * Every screen used to be titled "Webly", which is the whole of the tab bar for somebody with two sites open, the
 * whole of their browser history, and the first thing a screen reader says about a page that tells them nothing
 * about which page it is (WCAG 2.4.2). The screen's name is on its route; the site's is not known until the site
 * has loaded, so it is read from `SiteService.current` and the title is written when that arrives — or changes,
 * when somebody renames the site they are on.
 *
 * <b>Only the site the route names.</b> A switch changes the route before the next site has loaded, and `current`
 * is still the one being left for those few hundred milliseconds — so a title built from whatever is current
 * named the wrong site first and the right one after. Until the right one arrives, the title stays as it was.
 */
/**
 * The title after a navigation, for a polite live region in the app's root: a client-side navigation changes the
 * screen without a page load, so nothing else tells a screen reader it happened. Empty for the first page, whose
 * title the screen reader reads anyway, so that arriving is not announced twice. Its own service, with nothing to
 * inject, so the root component can read it without bringing the router and the API along.
 */
@Injectable({ providedIn: 'root' })
export class RouteAnnouncement {
  readonly text = signal('');
}

@Injectable({ providedIn: 'root' })
export class WeblyTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly sites = inject(SiteService);

  /** The route's own title, and the site it is a screen of, if any. Nothing until the first navigation. */
  private readonly page = signal<{ name?: string; site?: string } | undefined>(undefined);

  private readonly announcement = inject(RouteAnnouncement);

  private titled = false;

  constructor() {
    super();

    effect(() => {
      const page = this.page();

      if (!page) return;

      const { name, site } = page;
      const current = this.sites.current()?.summary;
      const siteName = site && current?.nanoid === site ? current.name : undefined;

      if (site && !siteName) return;

      const title = [name, siteName, 'Webly'].filter(Boolean).join(' · ');

      this.title.setTitle(title);

      if (this.titled) this.announcement.text.set(title);

      this.titled = true;
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.page.set({ name: this.buildTitle(snapshot), site: siteOf(snapshot.root) });
  }
}

/** The site the active branch is about, which is what the `:nanoid` parameter names. */
function siteOf(route: ActivatedRouteSnapshot | null): string | undefined {
  for (let current = route; current; current = current.firstChild) {
    const nanoid = current.paramMap.get('nanoid');

    if (nanoid) return nanoid;
  }

  return undefined;
}
