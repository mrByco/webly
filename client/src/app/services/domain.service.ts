import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesSiteNanoidDomainsGet$Json } from '../api/fn/domain/api-sites-site-nanoid-domains-get-json';
import { apiSitesSiteNanoidDomainsPost$Json } from '../api/fn/domain/api-sites-site-nanoid-domains-post-json';
import { apiSitesSiteNanoidDomainsDomainNanoidCheckPost$Json } from '../api/fn/domain/api-sites-site-nanoid-domains-domain-nanoid-check-post-json';
import { apiSitesSiteNanoidDomainsDomainNanoidPrimaryPost } from '../api/fn/domain/api-sites-site-nanoid-domains-domain-nanoid-primary-post';
import { apiSitesSiteNanoidDomainsDomainNanoidDelete } from '../api/fn/domain/api-sites-site-nanoid-domains-domain-nanoid-delete';
import { DomainResponse } from '../api/models/domain-response';

@Injectable({ providedIn: 'root' })
export class DomainService {
  private readonly api = inject(Api);

  list(siteNanoid: string): Promise<DomainResponse[]> {
    return this.api.invoke(apiSitesSiteNanoidDomainsGet$Json, { siteNanoid });
  }

  add(siteNanoid: string, hostname: string): Promise<DomainResponse> {
    return this.api.invoke(apiSitesSiteNanoidDomainsPost$Json, { siteNanoid, body: { hostname } });
  }

  /** Re-asks the provider. Driven by the person's click, never by a timer — see `CheckDomain`. */
  check(siteNanoid: string, domainNanoid: string): Promise<DomainResponse> {
    return this.api.invoke(apiSitesSiteNanoidDomainsDomainNanoidCheckPost$Json, { siteNanoid, domainNanoid });
  }

  setPrimary(siteNanoid: string, domainNanoid: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidDomainsDomainNanoidPrimaryPost, { siteNanoid, domainNanoid });
  }

  remove(siteNanoid: string, domainNanoid: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidDomainsDomainNanoidDelete, { siteNanoid, domainNanoid });
  }
}
