import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import AppThemeProvider from './AppThemeProvider'
import ColorModeMenu from './ColorModeMenu'

function renderMenu() {
  const user = userEvent.setup()
  render(
    <AppThemeProvider>
      <ColorModeMenu />
    </AppThemeProvider>,
  )

  return { user }
}

/** The theme-color tags index.html ships with, one per system scheme. */
function themeColors() {
  return [...document.head.querySelectorAll('meta[name="theme-color"]')].map(meta => meta.getAttribute('content'))
}

describe('ColorModeMenu', () => {
  beforeEach(() => {
    for (const scheme of ['light', 'dark']) {
      const meta = document.createElement('meta')
      meta.name = 'theme-color'
      meta.media = `(prefers-color-scheme: ${scheme})`
      meta.content = 'unset'
      document.head.append(meta)
    }
  })

  afterEach(() => {
    localStorage.clear()
    document.documentElement.className = ''
    document.head.querySelectorAll('meta[name="theme-color"]').forEach(meta => meta.remove())
  })

  it('ColorModeMenu_NoSavedChoice_FollowsSystemLightScheme', () => {
    renderMenu()

    expect(screen.getByRole('button', { name: 'Color mode: System' })).toBeInTheDocument()
    expect(themeColors()).toEqual(['#fff', '#fff'])
  })

  it('ColorModeMenu_ChooseDark_ShowsDarkModeAndDarkThemeColor', async () => {
    const { user } = renderMenu()

    await user.click(screen.getByRole('button', { name: 'Color mode: System' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }))

    expect(screen.getByRole('button', { name: 'Color mode: Dark' })).toBeInTheDocument()
    expect(themeColors()).toEqual(['#121212', '#121212'])
  })

  it('ColorModeMenu_SavedDarkChoice_StartsInDarkMode', async () => {
    const { user } = renderMenu()
    await user.click(screen.getByRole('button', { name: 'Color mode: System' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }))
    cleanup()

    renderMenu()

    expect(screen.getByRole('button', { name: 'Color mode: Dark' })).toBeInTheDocument()
    expect(themeColors()).toEqual(['#121212', '#121212'])
  })

  it('ColorModeMenu_OpenAfterChoosingLight_MarksOnlyLightChecked', async () => {
    const { user } = renderMenu()
    await user.click(screen.getByRole('button', { name: 'Color mode: System' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'Light' }))

    await user.click(screen.getByRole('button', { name: 'Color mode: Light' }))

    expect(screen.getByRole('menuitemradio', { name: 'Light' })).toBeChecked()
    expect(screen.getByRole('menuitemradio', { name: 'System' })).not.toBeChecked()
    expect(screen.getByRole('menuitemradio', { name: 'Dark' })).not.toBeChecked()
  })
})
