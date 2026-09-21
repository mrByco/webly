import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Icon } from '../../shared/icon';
import { Modal } from '../../components/modal/modal';
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
  imports: [FormsModule, Icon, Modal],
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

  /**
   * The open site, read from the service's signal rather than copied into one here: promoting a domain changes
   * the site's address, and a snapshot taken in the constructor would still be showing the old one.
   */
  protected readonly site = computed(() => this.sites.current());

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
    return this.run(() => this.domains.setPrimary(this.siteNanoid, domain.nanoid));
  }

  /** Which main address the dialog is about, or nothing. Any other row goes straight through. */
  protected readonly confirming = signal<DomainResponse | undefined>(undefined);

  /** What the address falls back to, which is what the dialog has to name. */
  protected readonly weblyUrl = computed(() => this.sites.current()?.summary.weblyUrl ?? '');

  protected confirmRemove(domain: DomainResponse): Promise<void> | void {
    if (!domain.isPrimary) return this.run(() => this.domains.remove(this.siteNanoid, domain.nanoid));

    this.confirming.set(domain);
  }

  protected async remove(): Promise<void> {
    const domain = this.confirming();

    if (!domain) return;

    await this.run(() => this.domains.remove(this.siteNanoid, domain.nanoid));

    this.confirming.set(undefined);
  }

  /**
   * The last thing copied, so the button can say so for a moment.
   *
   * Pressing a copy button used to do nothing anybody could see: the clipboard changed and the screen did
   * not, which on the one screen whose whole job is copying reads as a button that does not work. Keyed on
   * the value rather than a boolean per row, because a row has two of these.
   */
  protected readonly copied = signal<string | undefined>(undefined);

  protected copy(value: string | null | undefined): void {
    if (!value) return;

    void navigator.clipboard?.writeText(value);

    this.copied.set(value);

    // Long enough to be seen, short enough that it is gone before they come back from the registrar.
    setTimeout(() => this.copied.update(current => (current === value ? undefined : current)), 1500);
  }

  /**
   * Every mutation ends with a reload of both the list and the site, because the provider's answer is what
   * the row says next — and because half of what happens on this screen changes the site's **address**.
   *
   * The site reload used to be on `setPrimary` alone, and then removing the main address left the header and
   * the line above the list still naming a domain that had just been disconnected. Two callers, one of which
   * remembered: that is a rule that belongs in the one place both go through.
   */
  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(undefined);

    try {
      await action();
      await this.load();
      await this.sites.reload();
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
