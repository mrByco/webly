import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SiteImageResponse } from '../api/models/site-image-response';
import { UploadImagesResponse } from '../api/models/upload-images-response';
import { Api } from '../api/api';
import { apiSitesSiteNanoidImagesGet$Json } from '../api/fn/site-image/api-sites-site-nanoid-images-get-json';
import { apiSitesSiteNanoidImagesFileNameDelete } from '../api/fn/site-image/api-sites-site-nanoid-images-file-name-delete';

@Injectable({ providedIn: 'root' })
export class ImageService {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);

  /**
   * Where the editor loads a thumbnail from — <b>not</b> where the site's own pages do.
   *
   * A published page serves its photographs from its own domain, out of the export. This route is the
   * editor's, and it exists for the one thing the site's URL cannot do: show a picture from a site nobody has
   * published yet, or whose sandbox is asleep. Built as a string rather than fetched through the generated
   * client because it goes straight into an `<img src>`, where the browser sends the session cookie itself.
   */
  contentUrl(siteNanoid: string, fileName: string): string {
    return `/api/sites/${encodeURIComponent(siteNanoid)}/images/${encodeURIComponent(fileName)}`;
  }

  /** Removes it, as a version. Refused with a 409 naming the pages while one still uses it. */
  remove(siteNanoid: string, fileName: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidImagesFileNameDelete, { siteNanoid, fileName });
  }

  list(siteNanoid: string): Promise<SiteImageResponse[]> {
    return this.api.invoke(apiSitesSiteNanoidImagesGet$Json, { siteNanoid });
  }

  /**
   * The one call in this app that does not go through the generated client.
   *
   * `ng-openapi-gen` describes a multipart body as a typed object, and what has to be sent is a `FormData`
   * with one entry per file under the same name — which is what an `IFormFileCollection` binds from and what
   * the generated shape cannot express. Hand-written, and the URL is built the way the generated code builds
   * its own: relative, same origin, cookies by construction.
   */
  upload(siteNanoid: string, files: File[]): Promise<UploadImagesResponse> {
    const body = new FormData();

    for (const file of files) body.append('files', file, file.name);

    return firstValueFrom(
      this.http.post<UploadImagesResponse>(
        `/api/sites/${encodeURIComponent(siteNanoid)}/images`,
        body,
      ),
    );
  }
}
