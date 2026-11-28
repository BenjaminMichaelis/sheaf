import { describe, expect, it } from 'vitest'
import { fullName, splitName } from './names'

describe('splitName', () => {
  it('splitName_SeveralWords_LastWordIsLastName', () => {
    expect(splitName('  Mary  Ann Smith ')).toEqual({ firstName: 'Mary Ann', lastName: 'Smith' })
  })

  it('splitName_OneWord_FirstNameOnly', () => {
    expect(splitName('Cher')).toEqual({ firstName: 'Cher', lastName: null })
  })
})

describe('fullName', () => {
  it('fullName_WithoutLastName_ShowsFirstNameOnly', () => {
    expect(fullName({ firstName: 'Cher', lastName: null })).toBe('Cher')
    expect(fullName({ firstName: 'Mary Ann', lastName: 'Smith' })).toBe('Mary Ann Smith')
  })
})
