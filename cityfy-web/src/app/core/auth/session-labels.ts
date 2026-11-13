/**
 * "oggi alle 15:40", "domani alle 15:40", "ieri alle 15:40" or "il 15/11 alle 15:40" (local time):
 * when an anonymous session expires, when the last play arrived.
 */
export function dateTimeLabel(iso: string, now: Date = new Date()): string {
  const at = new Date(iso);
  const time = at.toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' });
  const days = Math.round((startOfDay(at) - startOfDay(now)) / 86_400_000);
  if (days === 0) return `oggi alle ${time}`;
  if (days === 1) return `domani alle ${time}`;
  if (days === -1) return `ieri alle ${time}`;
  return `il ${at.toLocaleDateString('it-IT', { day: '2-digit', month: '2-digit' })} alle ${time}`;
}

function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}
