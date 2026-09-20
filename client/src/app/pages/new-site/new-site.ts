import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { OnboardingLayout } from '../../components/onboarding-layout/onboarding-layout';
import { AuthService } from '../../services/auth.service';
import { SiteService } from '../../services/site.service';
import { messageOf } from '../../models/problem-details';

/**
 * Creating a site: one field, then straight into the editor.
 *
 * Full-screen, in the onboarding layout, because for most people this is the last step of signing up
 * and a nav bar offering three ways out is an invitation to leave halfway through.
 *
 * It asks for a name and nothing else. The temptation is a form — industry, colour, pages, tone — but
 * every one of those questions is one the agent can ask in the chat, in context, and answer for them if
 * they do not care. A five-field form before anybody has seen anything is where people close the tab.
 */
@Component({
  selector: 'app-new-site',
  imports: [FormsModule, OnboardingLayout],
  templateUrl: './new-site.html',
})
export class NewSitePage {
  private readonly sites = inject(SiteService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected name = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | undefined>(undefined);

  protected async create(): Promise<void> {
    if (this.saving() || this.name.trim().length === 0) {
      return;
    }

    this.saving.set(true);
    this.error.set(undefined);

    try {
      const site = await this.sites.create(this.name.trim());

      // The first site is what flips `hasSite`, which is what the onboarding chain reads. Folded into
      // the held profile rather than re-fetched: the answer is one field and we already know it.
      this.auth.patchMe({ hasSite: true, currentSiteNanoid: site.nanoid, currentSiteName: site.name });

      await this.router.navigateByUrl(AppRoutes.site.build(site.nanoid));
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.saving.set(false);
    }
  }
}
