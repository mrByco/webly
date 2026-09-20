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
import { apiSitesNanoidVersionsVersionNanoidRestorePost$Json } from '../api/fn/site/api-sites-nanoid-versions-version-nanoid-restore-post-json';
import { apiSitesNanoidSectionsPost$Json } from '../api/fn/site/api-sites-nanoid-sections-post-json';
import { apiSitesCatalogueGet$Json } from '../api/fn/site/api-sites-catalogue-get-json';
import { SectionSchemaResponse } from '../api/models/section-schema-response';
import { SiteDetailResponse } from '../api/models/site-detail-response';
import { SiteSummaryResponse } from '../api/models/site-summary-response';
import { SiteVersionResponse } from '../api/models/site-version-response';

/**
 * Sites, their versions and their sections.
 *
 * The section catalogue is cached for the lifetime of the tab: it changes when Webly is deployed, not
 * while somebody is editing, and the property editor asks for it every time a section is selected.
 */
@Injectable({ providedIn: 'root' })
export class SiteService {
  private readonly api = inject(Api);

  private catalogue?: Promise<SectionSchemaResponse[]>;

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

  async restore(nanoid: string, versionNanoid: string): Promise<SiteVersionResponse> {
    const version = await this.api.invoke(apiSitesNanoidVersionsVersionNanoidRestorePost$Json, {
      nanoid,
      versionNanoid,
    });

    await this.reload();

    return version;
  }

  /**
   * A hand edit. `props` is a patch: only the fields it names change, which is what lets the property
   * editor send one field on blur without resending a document it may have read before the agent's
   * last turn.
   */
  async editSection(
    nanoid: string,
    sectionId: string,
    props: Record<string, unknown>,
  ): Promise<SiteVersionResponse> {
    const version = await this.api.invoke(apiSitesNanoidSectionsPost$Json, {
      nanoid,
      body: { sectionId, props },
    });

    await this.reload();

    return version;
  }

  sectionCatalogue(): Promise<SectionSchemaResponse[]> {
    return (this.catalogue ??= this.api.invoke(apiSitesCatalogueGet$Json));
  }

  /**
   * The preview URL for a page of a version, or of the draft when no version is named.
   *
   * Built here because it is the one URL in the app that is not a call through the generated client:
   * the iframe loads it itself, and it is served by the same renderer that publishes the site — see
   * `PreviewSite` on the backend. Same origin, so the session cookie goes with it.
   */
  previewUrl(nanoid: string, page = '/', version?: string): string {
    const query = new URLSearchParams({ page });

    if (version) {
      query.set('version', version);
    }

    return `/api/sites/${encodeURIComponent(nanoid)}/preview?${query.toString()}`;
  }
}
