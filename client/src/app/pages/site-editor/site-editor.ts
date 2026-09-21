import { Component, ElementRef, PLATFORM_ID, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
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

  /** Bumped when the whole tree changed under the preview — a commit or a restore. Hot reload does the rest. */
  protected readonly previewKey = signal(0);

  /**
   * Whether the site's workspace is warm, and what it is doing if it is still starting. Two signals rather
   * than one nullable, because "asleep" and "starting" are different sentences on screen and only the
   * second one has a spinner.
   */
  protected readonly workspaceReady = signal(false);
  protected readonly workspaceProgress = signal<string | undefined>(undefined);

  protected readonly publishing = signal(false);
  protected readonly publishStatus = signal<string | undefined>(undefined);

  protected readonly canPublish = computed(() => {
    const summary = this.site()?.summary;

    return !!summary && (summary.hasUnpublishedChanges || !summary.publishedAt) && !this.publishing();
  });

  /** What the chat's `versionCommitted` output lands on: refresh the header, re-fetch the preview. */
  protected onVersionCommitted(): void {
    void this.sites.reload();

    // The dev server has already hot-reloaded the edits; this is for the case where the tree moved as a
    // whole, which a reload of the frame is the only way to be sure of.
    this.previewKey.update(key => key + 1);
  }

  /** The workspace is starting. Shown in the preview pane, because that is the thing that is missing. */
  protected onWorkspaceProgress(detail: string): void {
    this.workspaceProgress.set(detail);
  }

  /**
   * A turn ended. The workspace it warmed up outlives it, so the preview can load now — and the site row
   * is re-read for the same reason the header needs it: publishing state may have moved.
   */
  protected onTurnFinished(): void {
    this.workspaceProgress.set(undefined);
    this.workspaceReady.set(true);
    void this.sites.reload();
  }

  /**
   * The tab row, found in the DOM rather than with a `viewChild`: the row is inside an `@if` and projected into
   * `<app-shell>`, and the query resolved to undefined every time. One `querySelector` from the host does not
   * care where in the tree the element ended up.
   */
  private readonly host = inject(ElementRef<HTMLElement>);

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

    // The open tab, brought into view. At phone width the five tabs are a row somebody swipes and the last of
    // them sits off the right edge, so arriving on Settings — from a link, a reload or the back gesture — left the
    // row showing Editor while the screen showed something else. Browser only; `block: 'nearest'` moves the row
    // sideways without scrolling the page, and the timeout is because the active class is written by
    // `routerLinkActive` after the navigation this is reacting to.
    if (isPlatformBrowser(inject(PLATFORM_ID))) {
      inject(Router).events
        .pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed())
        .subscribe(() => this.centreOpenTab());

      afterNextRender(() => this.centreOpenTab());
    }
  }

  private centreOpenTab(): void {
    setTimeout(() => {
      const row = (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('header nav');
      const open = row?.querySelector<HTMLElement>('.btn-active');

      if (!row || !open) return;

      // The row's own `scrollLeft`, rather than `scrollIntoView({ inline: 'center' })`, which did nothing here:
      // measured against the two rectangles it is one line and it cannot decide to scroll something else.
      const rowBox = row.getBoundingClientRect();
      const openBox = open.getBoundingClientRect();

      row.scrollLeft += openBox.left - rowBox.left - (rowBox.width - openBox.width) / 2;
    });
  }

  /**
   * Starts the workspace because somebody wants to look at their site, not because they changed it.
   *
   * The endpoint answers immediately and the workspace takes tens of seconds, so this polls the site — the same
   * `workspaceReady` the editor reads on load. Polling rather than a run on the hub: there is nothing to say
   * while it happens beyond "still starting", and a run would mean a second kind of thing to reconnect to.
   */
  protected async wakePreview(): Promise<void> {
    const nanoid = this.nanoid();

    if (!nanoid || this.workspaceProgress()) return;

    this.workspaceProgress.set('Waking up your site');

    try {
      const { alreadyRunning } = await this.sites.wake(nanoid);

      if (alreadyRunning) {
        this.workspaceReady.set(true);
        this.workspaceProgress.set(undefined);

        return;
      }

      // Two minutes, which is longer than a cold start has ever taken and short enough to stop rather than
      // spin for ever if the machine never arrives.
      for (let attempt = 0; attempt < 60; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 2000));

        const site = await this.sites.load(nanoid);

        if (site.workspaceReady) {
          this.workspaceReady.set(true);
          this.workspaceProgress.set(undefined);
          this.previewKey.update(key => key + 1);

          return;
        }
      }

      this.workspaceProgress.set(undefined);
      this.error.set('Your preview did not start. Try asking for a change instead.');
    } catch (failure) {
      this.workspaceProgress.set(undefined);
      this.error.set(messageOf(failure));
    }
  }

  private async load(nanoid: string): Promise<void> {
    try {
      const site = await this.sites.load(nanoid);
      this.workspaceReady.set(site.workspaceReady);
      this.workspaceProgress.set(undefined);
      this.previewKey.update(key => key + 1);
      this.error.set(undefined);

      // Again here, because on a cold load the header does not exist yet when the navigation ends: the template
      // is still showing "loading your site", so there is no tab row to scroll.
      this.centreOpenTab();
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
