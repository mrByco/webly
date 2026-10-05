import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { AppShell } from '../../components/app-shell/app-shell';
import { UsageService } from '../../services/usage.service';
import { UsageReportResponse } from '../../api/models/usage-report-response';
import { UsageOutcome } from '../../api/models/usage-outcome';
import { messageOf } from '../../models/problem-details';
import { compact, duration, money, niceCeiling } from '../../models/usage-format';

/**
 * What the platform costs to run, for its administrators: the model's calls and the sandboxes' time.
 *
 * One headline figure — the period's total — because that is the question somebody opens this to answer; the
 * tiles under it split it into the two lines it is made of, the columns spread it over the days, and the tables
 * say who and what it was for. The columns are one series (the day's total) so they need no legend, and their
 * numbers are one click away in a table, because a bar's height is a comparison, not a value.
 */
@Component({
  selector: 'app-admin-usage',
  imports: [AppShell, DatePipe, RouterLink],
  templateUrl: './admin-usage.html',
})
export class AdminUsagePage {
  private readonly usage = inject(UsageService);

  protected readonly routes = AppRoutes;
  protected readonly money = money;
  protected readonly compact = compact;
  protected readonly duration = duration;

  protected readonly periods = [7, 30, 90] as const;
  protected readonly days = signal<number>(30);
  protected readonly report = signal<UsageReportResponse | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly total = computed(() => {
    const totals = this.report()?.totals;

    return totals ? totals.modelCostUsd + totals.sandboxCostUsd : 0;
  });

  protected readonly averagePerTurn = computed(() => {
    const totals = this.report()?.totals;

    return totals && totals.turns > 0 ? totals.modelCostUsd / totals.turns : 0;
  });

  /** The columns, scaled to a clean top, with the three gridlines that label them. */
  protected readonly chart = computed(() => {
    const days = this.report()?.byDay ?? [];
    const top = niceCeiling(Math.max(0, ...days.map(day => day.modelCostUsd + day.sandboxCostUsd)));

    return {
      ticks: [top, top / 2, 0].map(value => ({ label: money(value), at: (value / top) * 100 })),
      columns: days.map(day => {
        const cost = day.modelCostUsd + day.sandboxCostUsd;

        return { ...day, cost, height: (cost / top) * 100 };
      }),
    };
  });

  constructor() {
    effect(() => void this.load(this.days()));
  }

  private async load(days: number): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.report.set(await this.usage.report(days));
    } catch (error) {
      this.error.set(messageOf(error, 'The report could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected outcomeClass(outcome: UsageOutcome): string {
    return outcome === 'Completed' ? 'badge-success' : outcome === 'Stopped' ? 'badge-warning' : 'badge-error';
  }
}
