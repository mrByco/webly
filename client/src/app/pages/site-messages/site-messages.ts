import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Icon } from '../../shared/icon';
import { SubmissionService } from '../../services/submission.service';
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
 */
@Component({
  selector: 'app-site-messages',
  imports: [DatePipe, Icon],
  templateUrl: './site-messages.html',
})
export class SiteMessagesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly submissions = inject(SubmissionService);

  protected readonly siteNanoid = this.route.parent?.snapshot.paramMap.get('nanoid') ?? '';

  protected readonly list = signal<FormSubmissionResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | undefined>(undefined);

  protected readonly empty = computed(() => !this.loading() && this.list().length === 0);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.list.set(await this.submissions.list(this.siteNanoid));
      this.error.set(undefined);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.loading.set(false);
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
