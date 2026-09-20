import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { SiteChat } from '../../components/site-chat/site-chat';
import { SitePreview } from '../../components/site-preview/site-preview';
import { Icon } from '../../shared/icon';
import { DeploymentService } from '../../services/deployment.service';
import { RealtimeService, RunEvent } from '../../services/realtime.service';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';

/**
 * The editor, and the shell every per-site screen renders inside.
 *
 * It owns the site — one load, one signal — so the header, the chat, the preview and whichever child route
 * is showing cannot disagree about what is open or whether it has unpublished changes. The children
 * (history, domains, settings) render over the preview pane rather than replacing the page, which is what
 * keeps the chat available while somebody is looking at their domains.
 *
 * Publishing lives here rather than in a settings screen because it is the thing people come back to do,
 * and because its progress is a run: the button starts a deployment and then watches it on the hub, the
 * same substrate the agent's turns use.
 */
@Component({
  selector: 'app-site-editor',
  imports: [AppShell, Icon, RouterLink, RouterLinkActive, RouterOutlet, SiteChat, SitePreview],
  templateUrl: './site-editor.html',
})
export class SiteEditorPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sites = inject(SiteService);
  private readonly deployments = inject(DeploymentService);
  private readonly realtime = inject(RealtimeService);

  protected readonly routes = AppRoutes;

  /**
   * Read from the parameter map rather than a snapshot: the router reuses this component between one site
   * and the next, and a snapshot would leave the editor showing the site somebody navigated away from.
   */
  protected readonly nanoid = toSignal(this.route.paramMap.pipe(map(params => params.get('nanoid') ?? '')), {
    initialValue: '',
  });

  protected readonly site = this.sites.current;
  protected readonly error = signal<string | undefined>(undefined);

  /** Bumped whenever the document changes, which is what re-fetches the preview iframe. */
  protected readonly previewKey = signal(0);

  protected readonly publishing = signal(false);
  protected readonly publishStatus = signal<string | undefined>(undefined);

  protected readonly canPublish = computed(() => {
    const summary = this.site()?.summary;

    return !!summary && (summary.hasUnpublishedChanges || !summary.publishedAt) && !this.publishing();
  });

  /** What the chat's `versionCommitted` output lands on: refresh the header, re-fetch the preview. */
  protected onVersionCommitted(): void {
    void this.sites.reload();
    this.previewKey.update(key => key + 1);
  }

  constructor() {
    // The load follows the route parameter rather than the component's lifetime, because the router reuses
    // this component between sites. The guard is what stops a re-render from re-fetching the same site.
    let loaded = '';

    effect(() => {
      const nanoid = this.nanoid();

      if (!nanoid || nanoid === loaded) {
        return;
      }

      loaded = nanoid;
      void this.load(nanoid);
    });
  }

  private async load(nanoid: string): Promise<void> {
    try {
      await this.sites.load(nanoid);
      this.previewKey.update(key => key + 1);
      this.error.set(undefined);
    } catch (failure) {
      this.error.set(messageOf(failure));
    }
  }

  /**
   * Publishes, then follows the deployment on the hub. The row comes back `Queued`; everything after that
   * arrives as a `Deploy` run whose id is the deployment's nanoid — which is what lets a reloaded page
   * re-attach to a publish that is still going.
   */
  protected async publish(): Promise<void> {
    const nanoid = this.nanoid();

    if (!nanoid || this.publishing()) {
      return;
    }

    this.publishing.set(true);
    this.publishStatus.set('Queued');
    this.error.set(undefined);

    try {
      const deployment = await this.deployments.publish(nanoid);
      const events = await this.realtime.watch('Deploy', deployment.nanoid);

      events.subscribe({ next: event => this.applyDeployEvent(event) });
    } catch (failure) {
      this.error.set(messageOf(failure));
      this.publishing.set(false);
      this.publishStatus.set(undefined);
    }
  }

  private applyDeployEvent(event: RunEvent): void {
    switch (event.type) {
      case 'DeploymentProgress':
        this.publishStatus.set(event.detail ?? undefined);
        break;

      case 'Completed':
        this.publishing.set(false);
        this.publishStatus.set('Live');
        void this.sites.reload();
        break;

      case 'Failed':
        this.publishing.set(false);
        this.publishStatus.set(undefined);
        this.error.set(event.error ?? 'Publishing failed.');
        break;
    }
  }
}
