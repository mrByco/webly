import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { AuthService } from '../../services/auth.service';
import { messageOf } from '../../models/problem-details';
import { siteLimitReached, siteLimitSentence } from '../../models/site-limit';
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
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly routes = AppRoutes;

  protected readonly list = signal<SiteSummaryResponse[]>([]);

  /** At the account's limit the header says so where its "New site" button was. See `models/site-limit.ts`. */
  protected readonly atLimit = computed(() => siteLimitReached(this.list().length, this.auth.me().maxSites));
  protected readonly limitSentence = computed(() => siteLimitSentence(this.auth.me().maxSites));
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
