import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiAdminUsageGet$Json } from '../api/fn/usage/api-admin-usage-get-json';
import { UsageReportResponse } from '../api/models/usage-report-response';

@Injectable({ providedIn: 'root' })
export class UsageService {
  private readonly api = inject(Api);

  /** What the platform cost over the last `days` days. Administrators only; anybody else gets a 403. */
  report(days: number): Promise<UsageReportResponse> {
    return this.api.invoke(apiAdminUsageGet$Json, { days });
  }
}
