/**
 * Formats an instant as a calendar date in the church's time zone, which is how the API defines church dates.
 * Until the church's zone is known it falls back to the browser's zone.
 */
export function formatChurchDate(instant: string, timeZoneId: string | null): string {
  return new Date(instant).toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: timeZoneId ?? undefined })
}
