import { Component, ElementRef, PLATFORM_ID, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Subscription, filter, map } from 'rxjs';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { SiteChat } from '../../components/site-chat/site-chat';
import { SitePreview } from '../../components/site-preview/site-preview';
import { Modal } from '../../components/modal/modal';
import { Icon } from '../../shared/icon';
import { DeploymentService } from '../../services/deployment.service';
import { RealtimeService, RunEvent } from '../../services/realtime.service';
import { SiteService } from '../../services/site.service';
import { messageOf, unreachable } from '../../models/problem-details';
import { onReturn } from '../../shared/on-return';

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
  imports: [AppShell, Icon, Modal, RouterLink, RouterLinkActive, RouterOutlet, SiteChat, SitePreview],
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

  /**
   * Why the last wake did not end in a preview, said in the preview pane beside the button that tries again — and
   * cleared by pressing it. It used to be the editor's banner, which nothing about the preview ever cleared: wake
   * it during a restart and "Webly cannot be reached right now" stayed over the screen long after Webly was back.
   */
  protected readonly previewFailure = signal<string | undefined>(undefined);

  protected readonly publishing = signal(false);
  protected readonly publishStatus = signal<string | undefined>(undefined);

  /** A publish this page watched has just finished, and the screen should say so once. */
  protected readonly justPublished = signal(false);

  /**
   * Which pane a screen narrower than `lg` shows — it has room for one. Below that width the template hides the
   * other; from `lg` up both are always drawn and this is never read.
   */
  protected readonly pane = signal<'chat' | 'preview'>('chat');

  /** The preview has changed since somebody on a narrow screen last looked at it. A word on the switch, not a jump. */
  protected readonly previewFresh = signal(false);

  protected showPane(pane: 'chat' | 'preview'): void {
    this.pane.set(pane);

    if (pane === 'preview') this.previewFresh.set(false);
  }

  /** Something worth seeing reached the preview; say so on the switch if the chat is what is on screen. */
  private markPreviewFresh(): void {
    if (this.pane() === 'chat') this.previewFresh.set(true);
  }

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
    this.markPreviewFresh();
  }

  /** The workspace is starting. Shown in the preview pane, because that is the thing that is missing. */
  protected onWorkspaceProgress(detail: string): void {
    this.workspaceProgress.set(detail);
  }

  /**
   * The workspace a turn was waiting for is up: show the preview now, so the agent's edits appear in it as they
   * are made. The frame is reloaded as well as revealed, because a frame that was showing an older tree — a
   * workspace re-seeded or a dev server restarted — has to start again from the new one.
   */
  protected onWorkspaceReady(): void {
    this.workspaceProgress.set(undefined);
    this.workspaceReady.set(true);
    this.previewKey.update(key => key + 1);
    this.markPreviewFresh();
  }

  /**
   * A turn ended. The site row is re-read for the header — publishing state may have moved — and whether the
   * preview can be shown is taken from that answer rather than assumed.
   *
   * It used to be assumed: "the workspace a turn warmed up outlives it". Not when the turn ended because the server
   * restarted under it — the workspace went with the process, and the pane drew a frame over a sandbox that no
   * longer existed. The answer is the sandbox's own (`workspaceReady` asks it), and after an ordinary turn it is
   * the same yes the assumption gave.
   */
  protected async onTurnFinished(): Promise<void> {
    this.workspaceProgress.set(undefined);

    await this.sites.reload();

    this.workspaceReady.set(this.site()?.workspaceReady ?? false);
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

    onReturn(() => void this.catchUp());

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
   * Somebody has started writing a message. A cold site is woken now, so the sandbox boots while they type and the
   * turn they are about to start finds it warm — rather than spending its first quarter-minute on a spinner after
   * Send. Costs, at worst, a sandbox for a message nobody sent, which the reaper closes after its idle time.
   */
  protected onComposing(): void {
    if (!this.workspaceReady() && !this.workspaceProgress()) void this.wakePreview();
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

    // Every signal this touches belongs to whichever site is open, and the router reuses this component between
    // sites — so after each wait, a site that is no longer on screen ends the loop and touches nothing. It did
    // not: the polls went on for two minutes after somebody clicked away, and each one made the site they had
    // left the open one again, header, preview and chat included.
    const here = () => this.nanoid() === nanoid;

    this.workspaceProgress.set('Waking up your site');
    this.previewFailure.set(undefined);

    try {
      const { alreadyRunning } = await this.sites.wake(nanoid);

      if (!here()) return;
      if (alreadyRunning) return this.previewStarted();

      // Two minutes, which is longer than a cold start has ever taken and short enough to stop rather than
      // spin for ever if the machine never arrives.
      let lost: unknown;

      for (let attempt = 0; attempt < 60; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 2000));

        if (!here()) return;

        // Webly going away while the machine starts — a restart, a deploy, somebody's train going into a tunnel —
        // is waited out rather than reported. This loop used to stop at the first failed poll and put "Webly cannot
        // be reached" over the editor, where it stayed long after Webly was back, above a preview still asleep.
        const site = await this.sites.load(nanoid).catch((failure: unknown) => {
          if (!unreachable(failure)) throw failure;

          lost = failure;

          return undefined;
        });

        // A turn sent meanwhile says so itself, with `WorkspaceReady` — possibly while this request was out — and
        // reloading the frame a second time for the same news is a flicker.
        if (!here() || this.workspaceReady()) return;

        if (!site) {
          this.workspaceProgress.set('Reconnecting to Webly');
          continue;
        }

        if (site.workspaceReady) return this.previewStarted();

        // Back, and still not ready — so asked again. A restart takes whatever it was starting with it: the new
        // process has no workspace for this site and nothing asking it for one, so polling alone would watch
        // `workspaceReady` stay false for the rest of the two minutes. When it was only the connection that went,
        // the start is still going and a second request waits on the registry's lock and finds it.
        if (lost) {
          lost = undefined;
          this.workspaceProgress.set('Waking up your site');

          const again = await this.sites.wake(nanoid);

          if (!here()) return;
          if (again.alreadyRunning) return this.previewStarted();
        }
      }

      this.workspaceProgress.set(undefined);
      this.previewFailure.set(
        lost ? messageOf(lost) : 'Your preview did not start. Try again, or ask for a change instead.');
    } catch (failure) {
      if (!here()) return;

      this.workspaceProgress.set(undefined);
      this.previewFailure.set(messageOf(failure, 'Your preview did not start.'));
    }
  }

  /**
   * Somebody came back to this tab, so the site is read again: what another tab or device did meanwhile — a
   * version, a publish it started, a sandbox the reaper closed — would otherwise stay invisible here until a reload.
   * A publish going on elsewhere is joined, the way a reload joins one. A wake this page is running is left to
   * finish, since it is already polling the same thing.
   */
  private async catchUp(): Promise<void> {
    const nanoid = this.nanoid();

    if (!nanoid || this.workspaceProgress()) return;

    const site = await this.sites.load(nanoid).catch(() => undefined);

    if (!site || this.nanoid() !== nanoid || this.workspaceProgress()) return;

    // Only the change that matters to the frame: asleep to awake gets a fresh one, awake to asleep shows the
    // sentence instead of a frame over a dev server that is gone.
    if (site.workspaceReady !== this.workspaceReady()) {
      this.workspaceReady.set(site.workspaceReady);

      if (site.workspaceReady) this.previewKey.update(key => key + 1);
    }

    const active = site.activeDeploymentNanoid;

    if (active && active !== this.deployment?.nanoid) await this.watchDeployment(nanoid, active, 'Publishing');
  }

  /** The end of a wake that worked: a new frame, at an address no earlier one has loaded. */
  private previewStarted(): void {
    this.workspaceReady.set(true);
    this.workspaceProgress.set(undefined);
    this.previewKey.update(key => key + 1);
  }

  private async load(nanoid: string): Promise<void> {
    void this.releaseDeployment();

    try {
      const site = await this.sites.load(nanoid);

      // Somebody who clicks on to another site before this answers has asked a newer question.
      if (this.nanoid() !== nanoid) return;

      this.workspaceReady.set(site.workspaceReady);
      this.workspaceProgress.set(undefined);
      this.previewFailure.set(undefined);
      this.justPublished.set(false);
      this.previewFresh.set(false);
      this.previewKey.update(key => key + 1);
      this.error.set(undefined);

      // Again here, because on a cold load the header does not exist yet when the navigation ends: the template
      // is still showing "loading your site", so there is no tab row to scroll.
      this.centreOpenTab();

      if (site.activeDeploymentNanoid) await this.watchDeployment(nanoid, site.activeDeploymentNanoid, 'Publishing');
    } catch (failure) {
      if (this.nanoid() === nanoid) this.error.set(messageOf(failure));
    }
  }

  /**
   * Re-attaches to a publish that is already running.
   *
   * A deployment's run id *is* its nanoid, which is what makes a publish the one run here that outlives the
   * process that started it — and nothing used that, because nothing on load knew one was in flight. Reload the
   * editor mid-publish and the button read "Publish" again, over a publish that was already going. Pressing it
   * was safe, since the partial unique index refuses a second and `PublishSite` hands back the one that is
   * running, but the screen was lying until somebody pressed. Found by reloading during a publish.
   *
   * The same two steps the chat takes for the same reason — start, then watch — which is why a reload can join
   * either one.
   */
  private async watchDeployment(site: string, nanoid: string, status: string): Promise<void> {
    if (this.nanoid() !== site) return;

    this.publishing.set(true);

    // "Queued" is the truth when this call is what just created the row, and a guess when it is a reload
    // joining something already building — so the caller says which, rather than this one claiming a stage it
    // cannot know. The first `DeploymentProgress` replaces it either way.
    this.publishStatus.set(status);

    // Checked per event as well as after the wait, because the replay arrives *during* it.
    const events = await this.realtime.watch('Deploy', nanoid, {
      next: event => {
        if (this.nanoid() !== site) return;

        this.applyDeployEvent(event);

        // Over, so let go of it, as the chat does. Kept, a finished run stayed watched for the life of the page,
        // and the next hub reconnect — any restart of Webly — found it evicted, errored its stream and announced
        // "Your site is live" again over a publish from an hour before.
        if (event.type === 'Completed' || event.type === 'Failed') void this.realtime.unwatch(nanoid);
      },
      error: () => {
        if (this.nanoid() === site) void this.rejoinDeployment();
      },
    });

    // Left while the hub answered: the publish carries on, and coming back joins it again the way a reload does.
    if (this.nanoid() !== site) {
      events.unsubscribe();

      return void this.realtime.unwatch(nanoid);
    }

    this.deployment?.events.unsubscribe();
    this.deployment = { nanoid, events };
  }

  /** The publish this page is following, held so that leaving the site can let go of it. */
  private deployment?: { nanoid: string; events: Subscription };

  /**
   * Stops following a publish, without stopping it — the chat's `detach`, for the same reason. The router reuses
   * this component between sites, and nothing let go of the run: publish one site, click on to another, and the
   * second site's Publish button read "Building…" for the first one's build and then told its owner "Your site is
   * live" over a site nobody had published. Coming back joins it again through `activeDeploymentNanoid`.
   */
  private async releaseDeployment(): Promise<void> {
    const deployment = this.deployment;

    this.deployment = undefined;
    this.publishing.set(false);
    this.publishStatus.set(undefined);
    this.confirmingStarter.set(false);

    if (deployment) {
      deployment.events.unsubscribe();
      await this.realtime.unwatch(deployment.nanoid);
    }
  }

  /**
   * The publish run went away while the connection was down — it finished and was evicted, or the server restarted
   * under it. The deployment row is the durable record, so it is read: a publish still going is joined again, and
   * one that ended says how, as it would have live. Without that last half the button simply went back to
   * "Publish", which reads as a publish that worked.
   */
  private async rejoinDeployment(): Promise<void> {
    const nanoid = this.nanoid();

    this.deployment = undefined;
    this.publishing.set(false);
    this.publishStatus.set(undefined);

    await this.sites.reload();

    if (this.nanoid() !== nanoid) return;

    const active = this.site()?.activeDeploymentNanoid;

    if (active) {
      await this.watchDeployment(nanoid, active, 'Publishing');
    } else if (nanoid) {
      const [latest] = await this.deployments.list(nanoid);

      if (this.nanoid() !== nanoid) return;

      if (latest?.status === 'Failed') this.error.set(latest.error ?? 'Publishing failed.');
      if (latest?.status === 'Ready') this.justPublished.set(true);
    }
  }

  /**
   * Publishes, then follows the deployment on the hub. The row comes back `Queued`; everything after that
   * arrives as a `Deploy` run whose id is the deployment's nanoid — which is what lets a reloaded page
   * re-attach to a publish that is still going.
   */
  /** The site is about to go live for the first time as Webly's starter page. See `publish`. */
  protected readonly confirmingStarter = signal(false);

  /**
   * Publishes — unless this would be the first publish of a site nothing has been written for, in which case it asks.
   *
   * A new site's pages speak to its owner ("tell Webly what this site is about, and this page will be rewritten for
   * you"), because that is who reads them in the preview. Publish is in the header from the first second, so
   * pressing it before describing the business put that sentence on the public web, at an address the owner may
   * already have handed out. Asked rather than refused: publishing early is theirs to choose, and only the first
   * time — after that the site has been seen by the world either way. "Nothing has been written" is no version from
   * the assistant, read from the history only when it can matter.
   */
  protected async publish(confirmed = false): Promise<void> {
    const nanoid = this.nanoid();

    if (!nanoid || this.publishing()) {
      return;
    }

    if (!confirmed && !this.site()?.summary.publishedAt) {
      const versions = await this.sites.versions(nanoid).catch(() => []);

      // A question about the site somebody has since left would be answered about the one they are on now.
      if (this.nanoid() !== nanoid) return;

      if (!versions.some(version => version.origin === 'Agent')) {
        this.confirmingStarter.set(true);

        return;
      }
    }

    this.confirmingStarter.set(false);

    // Set before the request, not after it: the round trip is long enough for a second click, and `canPublish`
    // reads this. Two publishes are refused by the index anyway, but a button that stays pressable is how
    // somebody finds that out.
    this.publishing.set(true);
    this.justPublished.set(false);
    this.error.set(undefined);

    try {
      const deployment = await this.deployments.publish(nanoid);

      await this.watchDeployment(nanoid, deployment.nanoid, 'Queued');
    } catch (failure) {
      if (this.nanoid() !== nanoid) return;

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
        this.publishStatus.set(undefined);
        this.justPublished.set(true);
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
