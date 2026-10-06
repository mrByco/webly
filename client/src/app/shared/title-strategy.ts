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
 * has loaded, so it is read from `SiteService.current` and the title is written again when that arrives — or
 * changes, when somebody switches sites or renames the one they are on.
 */
@Injectable({ providedIn: 'root' })
export class WeblyTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly sites = inject(SiteService);

  /** The route's own title, and whether it is a screen of one site. */
  private readonly page = signal<{ name?: string; underSite: boolean }>({ underSite: false });

  constructor() {
    super();

    effect(() => {
      const { name, underSite } = this.page();
      const site = underSite ? this.sites.current()?.summary.name : undefined;

      this.title.setTitle([name, site, 'Webly'].filter(Boolean).join(' · '));
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.page.set({ name: this.buildTitle(snapshot), underSite: hasSite(snapshot.root) });
  }
}

/** Whether any route on the active branch names a site — which is what the `:nanoid` parameter is. */
function hasSite(route: ActivatedRouteSnapshot | null): boolean {
  for (let current = route; current; current = current.firstChild) {
    if (current.paramMap.has('nanoid')) return true;
  }

  return false;
}
