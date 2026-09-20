import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';
import { SiteSummaryResponse } from '../../api/models/site-summary-response';

/**
 * The site list, and the home screen.
 *
 * Also what a brand-new account sees: rather than a guard forcing somebody into a wizard, the empty
 * state offers the one thing there is to do. A guard would have to agree with the verification gate
 * about what comes first, and two guards that both redirect are how a loop happens.
 */
@Component({
  selector: 'app-sites',
  imports: [AppShell, Icon, RouterLink],
  templateUrl: './sites.html',
})
export class SitesPage {
  private readonly sites = inject(SiteService);
  private readonly router = inject(Router);

  protected readonly routes = AppRoutes;

  protected readonly list = signal<SiteSummaryResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | undefined>(undefined);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.list.set(await this.sites.list());
      this.error.set(undefined);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Opening a site records it as the one the editor comes back to. The navigation does not wait for
   * that: the pointer is a convenience, and making the click feel slower to save it is the wrong trade.
   */
  protected open(site: SiteSummaryResponse): void {
    void this.sites.open(site.nanoid).catch(() => undefined);
    void this.router.navigateByUrl(AppRoutes.site.build(site.nanoid));
  }
}
