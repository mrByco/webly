import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesSiteNanoidSubmissionsGet$Json } from '../api/fn/form-submission/api-sites-site-nanoid-submissions-get-json';
import { FormSubmissionResponse } from '../api/models/form-submission-response';

@Injectable({ providedIn: 'root' })
export class SubmissionService {
  private readonly api = inject(Api);

  /** Newest first, capped server-side. There is deliberately no paging yet — see `ListFormSubmissions`. */
  list(siteNanoid: string): Promise<FormSubmissionResponse[]> {
    return this.api.invoke(apiSitesSiteNanoidSubmissionsGet$Json, { siteNanoid });
  }
}
