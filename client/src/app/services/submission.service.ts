import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesSiteNanoidSubmissionsGet$Json } from '../api/fn/form-submission/api-sites-site-nanoid-submissions-get-json';
import { apiSitesSiteNanoidSubmissionsReadPost } from '../api/fn/form-submission/api-sites-site-nanoid-submissions-read-post';
import { apiSitesSiteNanoidSubmissionsSubmissionNanoidDelete } from '../api/fn/form-submission/api-sites-site-nanoid-submissions-submission-nanoid-delete';
import { FormSubmissionResponse } from '../api/models/form-submission-response';

@Injectable({ providedIn: 'root' })
export class SubmissionService {
  private readonly api = inject(Api);

  /** Newest first, capped server-side. There is deliberately no paging yet — see `ListFormSubmissions`. */
  list(siteNanoid: string): Promise<FormSubmissionResponse[]> {
    return this.api.invoke(apiSitesSiteNanoidSubmissionsGet$Json, { siteNanoid });
  }

  /**
   * Says the owner has had the list in front of them. Separate from `list` on purpose: reading is a GET and a
   * GET that clears somebody's unread messages is one a prefetch or a second tab can spend.
   */
  markRead(siteNanoid: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidSubmissionsReadPost, { siteNanoid });
  }

  remove(siteNanoid: string, submissionNanoid: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidSubmissionsSubmissionNanoidDelete, { siteNanoid, submissionNanoid });
  }
}
