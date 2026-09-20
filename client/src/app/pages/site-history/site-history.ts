import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { SitePreview } from '../../components/site-preview/site-preview';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';
import { SiteVersionResponse } from '../../api/models/site-version-response';

/**
 * The site's history: every version, what changed, and what the site looked like then.
 *
 * Restoring copies a version forward rather than moving a pointer back — see `RestoreSiteVersion` — so the
 * button is worded as an action that adds to the history, not one that rewinds it. Nothing here can lose
 * work, which is what makes it safe to offer without a confirmation dialog on every row.
 */
@Component({
  selector: 'app-site-history',
  imports: [DatePipe, Icon, SitePreview],
  templateUrl: './site-history.html',
})
export class SiteHistoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sites = inject(SiteService);

  protected readonly versions = signal<SiteVersionResponse[]>([]);
  protected readonly selected = signal<SiteVersionResponse | undefined>(undefined);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly restoring = signal(false);

  /** The parent route holds the site: this screen is a child of the editor shell. */
  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const versions = await this.sites.versions(this.siteNanoid);
      this.versions.set(versions);
      this.selected.set(versions.find(version => version.isDraft) ?? versions[0]);
      this.error.set(undefined);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  protected select(version: SiteVersionResponse): void {
    this.selected.set(version);
  }

  protected async restore(version: SiteVersionResponse): Promise<void> {
    if (this.restoring()) {
      return;
    }

    this.restoring.set(true);

    try {
      await this.sites.restore(this.siteNanoid, version.nanoid);
      await this.load();
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.restoring.set(false);
    }
  }
}
