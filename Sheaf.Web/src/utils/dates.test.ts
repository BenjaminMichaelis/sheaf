import { describe, expect, it } from 'vitest'
import { churchDateOf, formatChurchDate } from './dates'

describe('formatChurchDate', () => {
  it('formatChurchDate_InstantAfterMidnightInChurchZone_ShowsChurchCalendarDate', () => {
    // 06:30 UTC on March 1 is 00:30 in Chicago but still February 28 in Los Angeles.
    const instant = '2026-03-01T06:30:00Z'

    expect(formatChurchDate(instant, 'America/Chicago')).toBe(
      new Date(Date.UTC(2026, 2, 1, 12)).toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: 'UTC' }),
    )
    expect(formatChurchDate(instant, 'America/Los_Angeles')).toBe(
      new Date(Date.UTC(2026, 1, 28, 12)).toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: 'UTC' }),
    )
  })
})

describe('churchDateOf', () => {
  it('churchDateOf_InstantAfterMidnightInChurchZone_ReturnsChurchCalendarDate', () => {
    expect(churchDateOf('2026-03-01T06:30:00Z', 'America/Chicago')).toBe('2026-03-01')
    expect(churchDateOf('2026-03-01T06:30:00Z', 'America/Los_Angeles')).toBe('2026-02-28')
  })
})
