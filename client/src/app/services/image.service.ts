import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SiteImageResponse } from '../api/models/site-image-response';
import { UploadImagesResponse } from '../api/models/upload-images-response';
import { Api } from '../api/api';
import { apiSitesSiteNanoidImagesGet$Json } from '../api/fn/site-image/api-sites-site-nanoid-images-get-json';

@Injectable({ providedIn: 'root' })
export class ImageService {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);

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
