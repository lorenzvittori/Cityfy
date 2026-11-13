import { dateTimeLabel } from './session-labels';

describe('dateTimeLabel', () => {
  const now = new Date(2026, 10, 13, 15, 40);

  it('says "domani" for an expiry one day after the visit', () => {
    expect(dateTimeLabel(new Date(2026, 10, 14, 15, 40).toISOString(), now)).toBe('domani alle 15:40');
  });

  it('says "oggi" for later today', () => {
    expect(dateTimeLabel(new Date(2026, 10, 13, 22, 5).toISOString(), now)).toBe('oggi alle 22:05');
  });

  it('says "ieri" for a play received yesterday', () => {
    expect(dateTimeLabel(new Date(2026, 10, 12, 8, 30).toISOString(), now)).toBe('ieri alle 08:30');
  });

  it('shows the date further away', () => {
    expect(dateTimeLabel(new Date(2026, 10, 16, 9, 0).toISOString(), now)).toBe('il 16/11 alle 09:00');
  });
});
