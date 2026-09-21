import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Icon } from '../../shared/icon';
import { SiteService } from '../../services/site.service';
import { ImageService } from '../../services/image.service';
import { messageOf } from '../../models/problem-details';
import { SiteFileEntryResponse } from '../../api/models/site-file-entry-response';

/**
 * The site's source, read-only.
 *
 * "You never have to touch the code" is not "you are not allowed to see it", and the difference matters more
 * for this product than for most: the thing being sold is that a real Next.js project exists underneath, and a
 * claim nobody can check is a claim. It is also the screen a developer asks for within a minute of being shown
 * the product, and the answer "there is an API for that" is not one.
 *
 * Read-only, deliberately. Editing here would be a second way for the document to change — a second definition
 * of what a version is — and the way to change a site is to ask. If hand-editing ever lands, it lands as a
 * commit through `CommitSiteVersion` like everything else, not as a save button wired to a textarea.
 */
@Component({
  selector: 'app-site-code',
  imports: [Icon],
  templateUrl: './site-code.html',
})
export class SiteCodePage {
  private readonly route = inject(ActivatedRoute);
  private readonly sites = inject(SiteService);
  private readonly images = inject(ImageService);

  /** The parent route holds the site: this screen is a child of the editor shell. */
  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  protected readonly entries = signal<SiteFileEntryResponse[]>([]);
  protected readonly selected = signal<string | undefined>(undefined);
  protected readonly text = signal<string | undefined>(undefined);
  protected readonly size = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadingFile = signal(false);
  protected readonly error = signal<string | undefined>(undefined);

  /**
   * The flat list as a tree, one level of grouping by directory.
   *
   * One level rather than a real tree: a site is twenty-odd files in four directories, and a collapsible tree
   * for that is a component to maintain in exchange for nothing. If a site ever grows to the point where this
   * reads badly, that is the moment to write the tree — not before.
   */
  protected readonly groups = computed(() => {
    const byDirectory = new Map<string, SiteFileEntryResponse[]>();

    for (const entry of this.entries()) {
      const slash = entry.path.lastIndexOf('/');
      const directory = slash < 0 ? '' : entry.path.slice(0, slash);
      const files = byDirectory.get(directory);

      if (files) files.push(entry);
      else byDirectory.set(directory, [entry]);
    }

    return [...byDirectory.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([directory, files]) => ({
        directory,
        files: [...files].sort((a, b) => a.path.localeCompare(b.path)),
      }));
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const entries = await this.sites.files(this.siteNanoid);
      this.entries.set(entries);
      this.error.set(undefined);

      // Opens on the home page rather than on nothing: it is the file somebody came to look at, and an empty
      // pane beside a list is a screen that asks a question instead of answering one.
      const first = entries.find(entry => entry.path === 'src/app/page.tsx') ?? entries[0];

      if (first) await this.open(first.path);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  protected async open(path: string): Promise<void> {
    this.selected.set(path);
    this.loadingFile.set(true);
    this.text.set(undefined);

    try {
      const file = await this.sites.file(this.siteNanoid, path);

      // Guarded, because clicking down the list faster than the requests come back would otherwise leave one
      // file's contents beside another file's name.
      if (this.selected() === path) {
        this.text.set(file.text ?? undefined);
        this.size.set(file.size);
      }
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loadingFile.set(false);
    }
  }

  /** The last segment, for the list. The directory is already the group's heading. */
  protected name(path: string): string {
    const slash = path.lastIndexOf('/');

    return slash < 0 ? path : path.slice(slash + 1);
  }

  /** Bytes, roughly, for the one case the viewer cannot show: a file that is not text. */
  protected readonly humanSize = computed(() => {
    const bytes = this.size();

    return bytes < 1024 ? `${bytes} bytes` : `${Math.round(bytes / 1024)} KB`;
  });

  /**
   * The editor's URL for the selected file, when the selected file is one of the owner's photographs.
   *
   * Narrow on purpose, and in two ways. Only under `public/images/`, which is where uploads go and the one
   * place the image route can serve from; and only when the file really is not text, so a `.svg` — which
   * arrives as text and is shown as source, correctly, since it is source — is not routed through here.
   */
  protected readonly imageUrl = computed(() => {
    const path = this.selected();

    if (!path || this.text() !== undefined) return undefined;
    if (!path.startsWith('public/images/')) return undefined;

    return this.images.contentUrl(this.siteNanoid, path.slice('public/images/'.length));
  });

  protected readonly lines = computed(() => {
    const text = this.text();

    return text === undefined ? [] : text.split('\n');
  });
}
