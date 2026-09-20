import { Injectable, inject, signal } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesGet$Json } from '../api/fn/site/api-sites-get-json';
import { apiSitesPost$Json } from '../api/fn/site/api-sites-post-json';
import { apiSitesNanoidGet$Json } from '../api/fn/site/api-sites-nanoid-get-json';
import { apiSitesNanoidPut } from '../api/fn/site/api-sites-nanoid-put';
import { apiSitesNanoidOpenPost } from '../api/fn/site/api-sites-nanoid-open-post';
import { apiSitesNanoidDelete } from '../api/fn/site/api-sites-nanoid-delete';
import { apiSitesNanoidVersionsGet$Json } from '../api/fn/site/api-sites-nanoid-versions-get-json';
import { apiSitesNanoidVersionsVersionNanoidGet$Json } from '../api/fn/site/api-sites-nanoid-versions-version-nanoid-get-json';
import { apiSitesNanoidVersionsVersionNanoidDiffGet$Plain } from '../api/fn/site/api-sites-nanoid-versions-version-nanoid-diff-get-plain';
import { apiSitesNanoidVersionsVersionNanoidRestorePost$Json } from '../api/fn/site/api-sites-nanoid-versions-version-nanoid-restore-post-json';
import { apiSitesNanoidFilesGet$Json } from '../api/fn/site/api-sites-nanoid-files-get-json';
import { apiSitesNanoidFileGet$Json } from '../api/fn/site/api-sites-nanoid-file-get-json';
import { SiteDetailResponse } from '../api/models/site-detail-response';
import { SiteFileEntryResponse } from '../api/models/site-file-entry-response';
import { SiteFileResponse } from '../api/models/site-file-response';
import { SiteSummaryResponse } from '../api/models/site-summary-response';
import { SiteVersionResponse } from '../api/models/site-version-response';

/**
 * Sites, their history and their source.
 *
 * There is no "edit" method here and there is not meant to be one. A site's content is source code in a git
 * repository, and the only thing that writes it is an agent turn over the hub — so this service reads: the
 * site, its commits, a diff, a file. See `docs/domain-plan.md`.
 */
@Injectable({ providedIn: 'root' })
export class SiteService {
  private readonly api = inject(Api);

  /**
   * The site the editor is showing. Held here rather than in the page so that the chat, the preview
   * and the header all read one signal — a turn that commits a version updates this once and every
   * pane follows.
   */
  readonly current = signal<SiteDetailResponse | undefined>(undefined);

  list(): Promise<SiteSummaryResponse[]> {
    return this.api.invoke(apiSitesGet$Json);
  }

  create(name: string): Promise<SiteSummaryResponse> {
    return this.api.invoke(apiSitesPost$Json, { body: { name } });
  }

  async load(nanoid: string): Promise<SiteDetailResponse> {
    const site = await this.api.invoke(apiSitesNanoidGet$Json, { nanoid });
    this.current.set(site);

    return site;
  }

  /** Re-reads the open site. What a committed version, a publish or a rename ends with. */
  async reload(): Promise<void> {
    const nanoid = this.current()?.summary.nanoid;

    if (nanoid) {
      await this.load(nanoid);
    }
  }

  async rename(nanoid: string, name: string): Promise<void> {
    await this.api.invoke(apiSitesNanoidPut, { nanoid, body: { name } });
    await this.reload();
  }

  /** Makes this the site the app opens on. See `User.CurrentSiteId` on the backend. */
  open(nanoid: string): Promise<void> {
    return this.api.invoke(apiSitesNanoidOpenPost, { nanoid });
  }

  delete(nanoid: string): Promise<void> {
    return this.api.invoke(apiSitesNanoidDelete, { nanoid });
  }

  versions(nanoid: string, skip = 0, take = 50): Promise<SiteVersionResponse[]> {
    return this.api.invoke(apiSitesNanoidVersionsGet$Json, { nanoid, skip, take });
  }

  version(nanoid: string, versionNanoid: string): Promise<SiteVersionResponse> {
    return this.api.invoke(apiSitesNanoidVersionsVersionNanoidGet$Json, { nanoid, versionNanoid });
  }

  /** What one version changed, as a unified diff. Text, because that is what a diff is. */
  diff(nanoid: string, versionNanoid: string): Promise<string> {
    return this.api.invoke(apiSitesNanoidVersionsVersionNanoidDiffGet$Plain, { nanoid, versionNanoid });
  }

  async restore(nanoid: string, versionNanoid: string): Promise<SiteVersionResponse> {
    const version = await this.api.invoke(apiSitesNanoidVersionsVersionNanoidRestorePost$Json, {
      nanoid,
      versionNanoid,
    });

    await this.reload();

    return version;
  }

  /** The site's files at a commit, or at the head when no version is named. */
  files(nanoid: string, version?: string): Promise<SiteFileEntryResponse[]> {
    return this.api.invoke(apiSitesNanoidFilesGet$Json, { nanoid, version });
  }

  file(nanoid: string, path: string, version?: string): Promise<SiteFileResponse> {
    return this.api.invoke(apiSitesNanoidFileGet$Json, { nanoid, path, version });
  }

  /**
   * The preview's base URL — this origin, proxied to the site's own `next dev`.
   *
   * Built here rather than through the generated client because the iframe loads it itself, and because
   * everything under it is loaded by the page inside the frame: its chunks, its fonts and its hot-reload
   * socket all resolve relative to this path. The trailing slash is therefore load-bearing. Same origin,
   * so the session cookie goes with it and the sandbox's own address never reaches the browser.
   */
  previewUrl(nanoid: string): string {
    return `/api/sites/${encodeURIComponent(nanoid)}/preview/`;
  }
}
