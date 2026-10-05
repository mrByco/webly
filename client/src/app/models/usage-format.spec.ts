import { describe, expect, it } from 'vitest';
import { compact, duration, money, niceCeiling } from './usage-format';

describe('usage formatting', () => {
  it('writes cents for anything a person would round, and more for what would round to nothing', () => {
    expect(money(0)).toBe('$0.00');
    expect(money(0.2228)).toBe('$0.22');
    expect(money(1234.5)).toBe('$1,234.50');
    expect(money(0.0042)).toBe('$0.0042');
  });

  it('compacts large counts and leaves small ones alone', () => {
    expect(compact(950)).toBe('950');
    expect(compact(12_900)).toBe('12.9K');
    expect(compact(4_200_000)).toBe('4.2M');
  });

  it('says seconds, minutes or hours as the size calls for', () => {
    expect(duration(38)).toBe('38s');
    expect(duration(252)).toBe('4m 12s');
    expect(duration(3900)).toBe('1h 05m');
  });

  it('tops an axis at a clean number at or above the largest value', () => {
    expect(niceCeiling(0.74)).toBe(1);
    expect(niceCeiling(0.23)).toBe(0.5);
    expect(niceCeiling(1.3)).toBe(2);
    expect(niceCeiling(0)).toBe(1);
  });
});
