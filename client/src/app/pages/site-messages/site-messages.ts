import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Icon } from '../../shared/icon';
import { Modal } from '../../components/modal/modal';
import { SubmissionService } from '../../services/submission.service';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';
import { FormSubmissionResponse } from '../../api/models/form-submission-response';

/**
 * What visitors have sent from the published site.
 *
 * The one screen in this app that shows words nobody here wrote, which decides how it looks: a message is a
 * block of labelled answers in the order the visitor's form asked for them, not a row in a table with columns
 * this app chose. The labels are the site's own — the agent wrote the form — so a fixed "Name / Email /
 * Message" layout would be wrong for the first site that asks a fourth question.
 *
 * Loaded once when the screen opens and reloaded by a button, with no polling: an enquiry arrives every few
 * days on a site like this, the owner is notified by email, and a request a minute for the rest of the session
 * would be paid by every open editor tab in the product.
 *
 * **Opening this screen is what marks messages read.** Every message is shown in full, in a list, with nothing
 * to click through — so having the list in front of you is having read them, and a per-message button would be
 * asking somebody to confirm what they just did. The "New" marks come from the response that *preceded* the
 * acknowledgement, which is why the list is kept and not re-fetched afterwards: by then the server, correctly,
 * says everything has been read.
 */
@Component({
  selector: 'app-site-messages',
  imports: [DatePipe, Icon, Modal],
  templateUrl: './site-messages.html',
})
export class SiteMessagesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly submissions = inject(SubmissionService);
  private readonly sites = inject(SiteService);

  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  protected readonly list = signal<FormSubmissionResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | undefined>(undefined);

  protected readonly empty = computed(() => !this.loading() && this.list().length === 0);

  /** Which message the confirm dialog is about, or nothing. The page owns it, as every dialog here does. */
  protected readonly confirming = signal<FormSubmissionResponse | undefined>(undefined);
  protected readonly removing = signal(false);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const list = await this.submissions.list(this.siteNanoid);

      this.list.set(list);
      this.error.set(undefined);

      await this.acknowledge(list);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Tells the server these have been seen, and refreshes the site so the tab's badge agrees.
   *
   * Failing here is deliberately silent. The messages are on the screen either way, and an error banner over a
   * list that loaded perfectly, about a request the person did not make, is noise about nothing they can act
   * on — the worst that happens is the badge is still there next time, which is a smaller lie than the banner.
   */
  private async acknowledge(list: FormSubmissionResponse[]): Promise<void> {
    if (!list.some(x => !x.readAt)) return;

    try {
      await this.submissions.markRead(this.siteNanoid);
      await this.sites.reload();
    } catch {
      // Left unread. See above.
    }
  }

  protected async remove(): Promise<void> {
    const submission = this.confirming();

    if (!submission) return;

    this.removing.set(true);

    try {
      await this.submissions.remove(this.siteNanoid, submission.nanoid);
      this.list.update(list => list.filter(x => x.nanoid !== submission.nanoid));
      this.confirming.set(undefined);
      this.error.set(undefined);

      // A message deleted before it was ever acknowledged has to leave the badge too.
      if (!submission.readAt) await this.sites.reload();
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.removing.set(false);
    }
  }

  /**
   * A form name worth showing, or nothing. Every site's first form is called "contact", so printing that above
   * every message is a label that never distinguishes anything — while "quote" on a site with two forms is the
   * first thing the owner needs to know.
   */
  protected label(submission: FormSubmissionResponse): string | undefined {
    return submission.formName && submission.formName !== 'contact' ? submission.formName : undefined;
  }
}
