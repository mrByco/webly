import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Icon } from '../../shared/icon';
import { DomainService } from '../../services/domain.service';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';
import { DomainResponse } from '../../api/models/domain-response';

/**
 * Connecting a domain: add it, copy the DNS record, come back and check.
 *
 * The screen's whole job is the copying, which is why the record is shown as three labelled values rather
 * than a sentence — the person has their registrar's panel open in the next tab and is typing into three
 * boxes. The values are the provider's own, carried through unchanged; anything we paraphrased is
 * something they would type wrong.
 *
 * Checking is a button, never a timer. A poll per pending domain per minute is a provider rate limit
 * waiting to happen, and the person knows when they changed something.
 */
@Component({
  selector: 'app-site-domains',
  imports: [FormsModule, Icon],
  templateUrl: './site-domains.html',
})
export class SiteDomainsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly domains = inject(DomainService);
  private readonly sites = inject(SiteService);

  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  protected readonly list = signal<DomainResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | undefined>(undefined);

  /** The Webly subdomain, which every site keeps whatever custom domains it has. */
  protected readonly weblyUrl = signal(this.sites.current()?.summary.url ?? '');

  protected hostname = '';

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.list.set(await this.domains.list(this.siteNanoid));
      this.error.set(undefined);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  protected async add(): Promise<void> {
    const hostname = this.hostname.trim();

    if (!hostname || this.busy()) {
      return;
    }

    await this.run(async () => {
      await this.domains.add(this.siteNanoid, hostname);
      this.hostname = '';
    });
  }

  protected check(domain: DomainResponse): Promise<void> {
    return this.run(() => this.domains.check(this.siteNanoid, domain.nanoid));
  }

  protected setPrimary(domain: DomainResponse): Promise<void> {
    return this.run(async () => {
      await this.domains.setPrimary(this.siteNanoid, domain.nanoid);
      await this.sites.reload();
    });
  }

  protected remove(domain: DomainResponse): Promise<void> {
    return this.run(() => this.domains.remove(this.siteNanoid, domain.nanoid));
  }

  protected copy(value: string | null | undefined): void {
    if (value) {
      void navigator.clipboard?.writeText(value);
    }
  }

  /** Every mutation ends with a reload, because the provider's answer is what the row says next. */
  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(undefined);

    try {
      await action();
      await this.load();
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
