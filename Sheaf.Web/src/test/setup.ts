import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'
import { PHONE_WIDTH, setScreenWidth } from './screen'

// A page's first render waits on its sign-in, settings and data requests plus MUI's first paint, which can pass the
// default 1 second on a busy machine. findBy and waitFor still return as soon as the element appears.
configure({ asyncUtilTimeout: 3000 })

beforeEach(() => {
  setScreenWidth(PHONE_WIDTH)
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})
