import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'
import { PHONE_WIDTH, setScreenWidth } from './screen'

beforeEach(() => {
  setScreenWidth(PHONE_WIDTH)
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})
