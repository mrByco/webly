import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { Modal } from '../../components/modal/modal';
import { Icon } from '../../shared/icon';
import { DeploymentService } from '../../services/deployment.service';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';
import { DeploymentResponse } from '../../api/models/deployment-response';

/**
 * The site's own settings: its name, its publish history, and deleting it.
 *
 * The publish list lives here rather than in the History tab on purpose: the history is about what the site
 * said, and a list of deployments — including the failed ones — is about our infrastructure. Somebody
 * scrolling their site's past should not have to read past three retries of an upload.
 */
@Component({
  selector: 'app-site-settings',
  imports: [DatePipe, FormsModule, Icon, Modal],
  templateUrl: './site-settings.html',
})
export class SiteSettingsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sites = inject(SiteService);
  private readonly deployments = inject(DeploymentService);
  private readonly router = inject(Router);

  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';
  protected readonly site = this.sites.current;

  /** The repository download. A URL and a filename, because it is a link rather than a request. */
  protected readonly exportUrl = computed(() => this.sites.exportUrl(this.siteNanoid));
  protected readonly exportFileName = computed(() => `${this.site()?.summary.slug ?? 'site'}.bundle`);

  protected name = this.sites.current()?.summary.name ?? '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly history = signal<DeploymentResponse[]>([]);
  protected readonly confirmingDelete = signal(false);
  protected readonly republishing = signal(false);

  /**
   * What the assistant believes about this business, as it wrote it down.
   *
   * `content/brand.md` is the agent's memory — its session does not outlive a turn, so anything it learns
   * goes in that file and every later turn starts from it. That makes it the most consequential text in the
   * site and the only one nobody could see: a fact recorded wrongly ("closes at six" when it is five) shapes
   * every page written afterwards, and the person it belongs to had no way of knowing it was in there.
   *
   * Read-only, like the Code tab and for the same reason — a second way to change a site would be a second
   * definition of a version. Correcting it is a sentence in the chat, which is also how it got there.
   */
  protected readonly brand = signal<string | undefined>(undefined);
  protected readonly brandOpen = signal(false);

  /**
   * Whether "Publish again" is worth offering: the site has been published and there is nothing newer to
   * publish, which is exactly when the editor's own Publish button is unavailable. With unpublished changes
   * that button is the thing to press, and a second one beside it would only be a way to publish less.
   */
  protected readonly canRepublish = computed(() => {
    const summary = this.site()?.summary;

    return summary !== undefined && summary.publishedAt != null && !summary.hasUnpublishedChanges;
  });

  constructor() {
    void this.deployments
      .list(this.siteNanoid)
      .then(list => this.history.set(list))
      .catch(() => this.history.set([]));

    // Failure is silence: a site whose agent has never run, or whose agent deleted the file, simply has no
    // facts panel. An error about a missing file would be noise on a screen about something else.
    void this.sites
      .file(this.siteNanoid, 'content/brand.md')
      .then(file => this.brand.set(file.text ?? undefined))
      .catch(() => this.brand.set(undefined));
  }

  /**
   * Builds and uploads the live version again. For a deployment whose output the provider lost — or one this
   * app recorded as Ready and wrote somewhere it could not serve from. See `PublishSiteRequest.Republish`.
   */
  protected async republish(): Promise<void> {
    if (this.republishing()) {
      return;
    }

    this.republishing.set(true);
    this.error.set(undefined);

    try {
      const deployment = await this.deployments.publish(this.siteNanoid, true);
      this.history.update(list => [deployment, ...list]);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.republishing.set(false);
    }
  }

  protected async rename(): Promise<void> {
    const name = this.name.trim();

    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(undefined);

    try {
      await this.sites.rename(this.siteNanoid, name);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    this.saving.set(true);

    try {
      await this.sites.delete(this.siteNanoid);
      await this.router.navigateByUrl(AppRoutes.home.build());
    } catch (failure) {
      this.error.set(messageOf(failure));
      this.confirmingDelete.set(false);
    } finally {
      this.saving.set(false);
    }
  }
}
