/**
 * How the usage report writes money, counts and time. One place, because the same number appears in a tile, a
 * table and a tooltip, and three formatters would round it three ways.
 */

/**
 * Dollars. Two places for anything a person would round to cents, and enough to see for the turns that cost a
 * fraction of one — "$0.00" beside a turn that cost $0.004 reads as free, and a hundred of them are not.
 */
export function money(usd: number): string {
  if (usd === 0) return '$0.00';

  if (Math.abs(usd) < 0.01) return `$${usd.toFixed(4)}`;

  return `$${usd.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

/** 950 · 12.9K · 4.2M — a token count is read for its size, not its last digit. */
export function compact(count: number): string {
  if (Math.abs(count) < 1000) return String(count);

  return new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(count);
}

/** 38s · 4m 12s · 1h 05m — seconds are worth reading on a turn and noise on a day of sandbox time. */
export function duration(seconds: number): string {
  const whole = Math.round(seconds);

  if (whole < 60) return `${whole}s`;

  const minutes = Math.floor(whole / 60);

  if (minutes < 60) return `${minutes}m ${String(whole % 60).padStart(2, '0')}s`;

  return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m`;
}

/** A clean top for a column chart's axis: 1, 2 or 5 times a power of ten, at or above the largest value. */
export function niceCeiling(value: number): number {
  if (value <= 0) return 1;

  const power = 10 ** Math.floor(Math.log10(value));

  return ([1, 2, 5, 10].map(step => step * power).find(step => step >= value) ?? 10 * power);
}
