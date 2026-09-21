import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { DiffLineKind, parseUnifiedDiff } from '../../models/unified-diff';
import { messageOf } from '../../models/problem-details';
import { SiteVersionResponse } from '../../api/models/site-version-response';

/**
 * The site's history: every version, and what each one changed.
 *
 * <b>A version shows its diff, not a preview of itself.</b> There is one dev server per site and it runs the
 * working tree, so there is nothing to point an iframe at for a commit from last Tuesday — and standing one
 * up per version somebody clicks would be a machine per curiosity. The diff is also the better answer to
 * the question people actually have here, which is "what did that change".
 *
 * Restoring copies a version forward rather than moving a pointer back — see `RestoreSiteVersion` — so the
 * button is worded as an action that adds to the history, not one that rewinds it. Nothing here can lose
 * work, which is what makes it safe to offer without a confirmation dialog on every row.
 */
@Component({
  selector: 'app-site-history',
  imports: [DatePipe, Icon],
  templateUrl: './site-history.html',
})
export class SiteHistoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sites = inject(SiteService);

  protected readonly versions = signal<SiteVersionResponse[]>([]);
  protected readonly selected = signal<SiteVersionResponse | undefined>(undefined);
  protected readonly diff = signal<string | undefined>(undefined);
  protected readonly loading = signal(true);
  protected readonly loadingDiff = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly restoring = signal(false);

  /** The parent route holds the site: this screen is a child of the editor shell. */
  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  /**
   * The version the URL is asking for, as a signal rather than a snapshot. The chat sits beside this screen on
   * a wide window, so a second link is a query-parameter change on a route that is already open — which
   * re-creates nothing and would have left the pane showing the version somebody clicked a minute ago.
   */
  private readonly asked = toSignal(
    this.route.queryParamMap.pipe(map(parameters => parameters.get('version'))),
    { initialValue: this.route.snapshot.queryParamMap.get('version') });

  constructor() {
    void this.load();

    effect(() => {
      const asked = this.asked();
      const version = this.versions().find(candidate => candidate.nanoid === asked);

      if (version && version.nanoid !== this.selected()?.nanoid) {
        this.select(version);
      }
    });
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const versions = await this.sites.versions(this.siteNanoid);
      this.versions.set(versions);
      this.error.set(undefined);

      // `?version=` is how the chat links the turn that produced a version, so it outranks the head: somebody
      // following that link is asking about that one. An id that no longer names anything falls back rather
      // than showing an empty pane — a stale link should still open the history.
      const chosen = versions.find(version => version.nanoid === this.asked())
        ?? versions.find(version => version.isHead)
        ?? versions[0];

      if (chosen) {
        this.select(chosen);
      }
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  protected select(version: SiteVersionResponse): void {
    this.selected.set(version);
    void this.loadDiff(version);
  }

  private async loadDiff(version: SiteVersionResponse): Promise<void> {
    this.loadingDiff.set(true);
    this.diff.set(undefined);

    try {
      const diff = await this.sites.diff(this.siteNanoid, version.nanoid);

      // Guarded: clicking through the list faster than the requests come back would otherwise leave the
      // diff of whichever one answered last beside a row nobody selected.
      if (this.selected()?.nanoid === version.nanoid) {
        this.diff.set(diff);
      }
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loadingDiff.set(false);
    }
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

  /** The commit, short. A site's owner need not care, and the developer who clones it later will. */
  protected shortSha(sha: string): string {
    return sha.slice(0, 7);
  }

  /**
   * The diff, grouped by file. A computed signal rather than a method the template calls: a method returning a
   * fresh array on every change-detection pass would re-create every line's DOM node.
   *
   * Grouped because a restore moves every file a run of turns touched, and one `<pre>` of six files' worth of
   * hunks is a wall. Each file is a `<details>` — native, so which ones are open is the browser's business and
   * not a signal here — open by default, because the common turn changes one file and a person looking at it
   * should not have to click.
   */
  protected readonly diffFiles = computed(() => parseUnifiedDiff(this.diff() ?? ''));

  /** Whether there is anything to draw, as opposed to a diff that has not arrived yet. */
  protected readonly hasDiff = computed(() => this.diffFiles().length > 0);

  /**
   * Whether the file blocks start open. A turn changes one file and expanding it by hand would be a click for
   * nothing; the first commit of a site is eighteen, and opening all of them buries the list of what they are
   * under several thousand lines of template.
   */
  protected readonly expandByDefault = computed(() => this.diffFiles().length <= 3);

  protected classOf(kind: DiffLineKind): string {
    switch (kind) {
      case 'added': return 'bg-success/10 text-success';
      case 'removed': return 'bg-error/10 text-error';
      case 'hunk': return 'mt-2 text-info/80';
      default: return 'text-base-content/70';
    }
  }

  /** The marker a line would have carried in the raw diff, kept as a gutter so a copy is the file's own text. */
  protected markerOf(kind: DiffLineKind): string {
    switch (kind) {
      case 'added': return '+';
      case 'removed': return '−';
      case 'hunk': return '';
      default: return ' ';
    }
  }
}
