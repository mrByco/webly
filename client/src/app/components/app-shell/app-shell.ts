import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AuthService } from '../../services/auth.service';
import { SiteService } from '../../services/site.service';
import { Avatar } from '../avatar/avatar';
import { Icon } from '../../shared/icon';
import { SiteSummaryResponse } from '../../api/models/site-summary-response';

/**
 * The frame every signed-in screen sits in: a sidebar from `lg` up, a slim bottom bar below it.
 *
 * <b>The sidebar is the site switcher.</b> A person's sites are the app's top level — there are no
 * other areas — so listing them here is both the navigation and the answer to "which one am I
 * editing", and it is why this component fetches. Everything that is about one site (the editor, the
 * history, the domains) is a tab inside that site's own screen rather than a sidebar entry, because
 * those links cannot be built without knowing which site they mean.
 *
 * The reference project (cookta) needs a fifth "more" tab for the administrative screens its bottom
 * bar cannot hold. Webly has two destinations, so it does not — a tab leading to a page with two
 * links on it is a level of navigation that exists to be navigated.
 */
@Component({
  selector: 'app-shell',
  imports: [Avatar, Icon, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.html',
})
export class AppShell {
  private readonly auth = inject(AuthService);
  private readonly sites = inject(SiteService);

  protected readonly routes = AppRoutes;
  protected readonly me = this.auth.me;

  protected readonly mySites = signal<SiteSummaryResponse[]>([]);

  /** Whether to offer another site at all, so the limit is visible before it is hit. */
  protected readonly canCreate = computed(() => this.mySites().length < 3);

  constructor() {
    // Failure is silence: the shell is drawn around every screen, and a site list that could not be
    // fetched must not stop somebody reaching their account page to sign out.
    void this.sites
      .list()
      .then(sites => this.mySites.set(sites))
      .catch(() => this.mySites.set([]));
  }

  protected logout(): Promise<void> {
    return this.auth.signOutToLogin();
  }
}
