/**
 * Formats an instant as a calendar date in the church's time zone, which is how the API defines church dates.
 * Callers wait for the church's zone rather than guessing with the browser's, which can show a different day.
 */
export function formatChurchDate(instant: string, timeZoneId: string): string {
  return new Date(instant).toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: timeZoneId })
}
