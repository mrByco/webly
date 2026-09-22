import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { ImageService } from '../../services/image.service';
import { DiffFile, DiffLineKind, parseUnifiedDiff } from '../../models/unified-diff';
import { messageOf } from '../../models/problem-details';
import { isWideScreen } from '../../shared/wide-screen';
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
  private readonly images = inject(ImageService);
  private readonly sites = inject(SiteService);

  protected readonly versions = signal<SiteVersionResponse[]>([]);
  protected readonly selected = signal<SiteVersionResponse | undefined>(undefined);

  private readonly wide = isWideScreen();
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
      // following that link is asking about that one, and it opens on a phone as well — they asked for it.
      const asked = versions.find(version => version.nanoid === this.asked());

      // The head, only where there is a pane beside the list. Below `lg` the two stack and one shows at a
      // time, so opening on a diff would land somebody on a diff with the list hidden behind a back button.
      // An id that no longer names anything falls back to the list rather than to an empty pane.
      const chosen = asked ?? (this.wide ? versions.find(version => version.isHead) ?? versions[0] : undefined);

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

  /**
   * A photograph this version put into the site, as something to look at rather than a sentence about bytes.
   *
   * The same decision the Code tab made, on the screen that needs it more: this list is where somebody decides
   * what to bring back, and "Added lathe.png" followed by "there is nothing to show line by line" is a question
   * rather than an answer. It is the owner's own picture and a route for its bytes already exists.
   *
   * Asked for **at this version**, which is what the route's `version` parameter is for. Reading the head would
   * show the wrong photograph once a name has been reused, and a broken image as soon as one is deleted — on
   * the one screen whose whole job is to say what a version did.
   *
   * Narrow in three ways, each with a reason. Only a file git called binary, so an `.svg` stays the source it
   * is. Only under `public/images/`, which is where uploads go and the only place that route reads from. And
   * never for a **removed** file: it is not in this version's tree — that is what removed means — so the bytes
   * to show are the parent's, and a version's diff pointing into a different version is a thread to pull when
   * somebody asks for it rather than now.
   */
  protected imageFor(file: DiffFile): string | undefined {
    const version = this.selected();

    if (!version || !file.binary || file.change === 'removed') return undefined;
    if (!file.path.startsWith('public/images/')) return undefined;

    return this.images.contentUrl(
      this.siteNanoid, file.path.slice('public/images/'.length), version.nanoid);
  }

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
