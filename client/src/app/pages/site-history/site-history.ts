import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
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

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const versions = await this.sites.versions(this.siteNanoid);
      this.versions.set(versions);
      this.error.set(undefined);

      const head = versions.find(version => version.isHead) ?? versions[0];

      if (head) {
        this.select(head);
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
   * The diff, split for colouring. A computed signal rather than a method the template calls: a method
   * returning a fresh array on every change-detection pass would re-create every line's DOM node.
   *
   * Split and classed by hand rather than with a highlighting library — this is eleven lines of CSS, and
   * the client's dependency list is worth more than prettier gutters.
   */
  protected readonly diffLines = computed(() => {
    const diff = this.diff();

    // Trimmed first: the first commit has no parent and therefore no diff, and an empty string splits
    // into one empty line, which would draw a blank code block instead of saying so.
    return diff?.trim() ? diff.split('\n') : [];
  });

  protected classOf(line: string): string {
    if (line.startsWith('+++') || line.startsWith('---') || line.startsWith('diff ') || line.startsWith('index ')) {
      return 'font-semibold text-base-content/70';
    }

    if (line.startsWith('@@')) return 'text-info';
    if (line.startsWith('+')) return 'text-success';
    if (line.startsWith('-')) return 'text-error';

    return 'text-base-content/70';
  }
}
