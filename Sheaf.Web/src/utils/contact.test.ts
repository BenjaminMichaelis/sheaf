import { describe, expect, it } from 'vitest'
import { telHref } from './contact'

describe('telHref', () => {
  it.each([
    ['555-0100', 'tel:555-0100'],
    ['+1 (555) 010-0100', 'tel:+1(555)010-0100'],
    ['555-0100 ext 2', 'tel:555-0100'],
    ['555-0100 (cell)', 'tel:555-0100'],
    ['555-0100 #2', 'tel:555-0100'],
    ['555-0100 / 555-0199', 'tel:555-0100'],
  ])('telHref_%s_LinksTheDialableNumber', (phone, expected) => {
    expect(telHref(phone)).toBe(expected)
  })

  it.each(['1-800-FLOWERS', 'ask at the desk', ''])('telHref_%s_DoesNotLink', phone => {
    expect(telHref(phone)).toBeNull()
  })
})
