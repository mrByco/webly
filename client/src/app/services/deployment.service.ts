import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesSiteNanoidDeploymentsGet$Json } from '../api/fn/deployment/api-sites-site-nanoid-deployments-get-json';
import { apiSitesSiteNanoidDeploymentsPost$Json } from '../api/fn/deployment/api-sites-site-nanoid-deployments-post-json';
import { DeploymentResponse } from '../api/models/deployment-response';

@Injectable({ providedIn: 'root' })
export class DeploymentService {
  private readonly api = inject(Api);

  list(siteNanoid: string): Promise<DeploymentResponse[]> {
    return this.api.invoke(apiSitesSiteNanoidDeploymentsGet$Json, { siteNanoid });
  }

  /**
   * Queues a publish. The row that comes back is `Queued`, not done — progress arrives on the hub as
   * a `Deploy` run whose id is the deployment's nanoid, and the list is what a reloaded page reads
   * instead.
   */
  publish(siteNanoid: string): Promise<DeploymentResponse> {
    return this.api.invoke(apiSitesSiteNanoidDeploymentsPost$Json, { siteNanoid });
  }
}
