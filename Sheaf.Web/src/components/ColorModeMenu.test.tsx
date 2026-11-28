import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { theme } from '@/theme'
import { PHONE_WIDTH, setScreenWidth } from '@/test/screen'
import AppThemeProvider from './AppThemeProvider'
import ColorModeMenu from './ColorModeMenu'

// The requirement is that the browser chrome matches the page background, whatever the palette's colors are.
const LIGHT_BACKGROUND = theme.colorSchemes.light?.palette.background.default
const DARK_BACKGROUND = theme.colorSchemes.dark?.palette.background.default

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

  it('ColorModeMenu_NoSavedChoiceOnLightSystem_UsesSystemModeWithLightBackground', () => {
    renderMenu()

    expect(screen.getByRole('button', { name: 'Color mode: System' })).toBeInTheDocument()
    expect(themeColors()).toEqual([LIGHT_BACKGROUND, LIGHT_BACKGROUND])
  })

  it('ColorModeMenu_NoSavedChoiceOnDarkSystem_UsesSystemModeWithDarkBackground', () => {
    setScreenWidth(PHONE_WIDTH, 'dark')

    renderMenu()

    expect(DARK_BACKGROUND).not.toEqual(LIGHT_BACKGROUND)
    expect(screen.getByRole('button', { name: 'Color mode: System' })).toBeInTheDocument()
    expect(themeColors()).toEqual([DARK_BACKGROUND, DARK_BACKGROUND])
  })

  it('ColorModeMenu_ChooseDarkOnLightSystem_ShowsDarkModeAndDarkBackground', async () => {
    const { user } = renderMenu()

    await user.click(screen.getByRole('button', { name: 'Color mode: System' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }))

    expect(screen.getByRole('button', { name: 'Color mode: Dark' })).toBeInTheDocument()
    expect(themeColors()).toEqual([DARK_BACKGROUND, DARK_BACKGROUND])
  })

  // Remembering the choice is MUI's storage; this checks the requirement that a returning user keeps their choice.
  it('ColorModeMenu_ChoiceFromEarlierVisit_IsRestoredOnNextVisit', async () => {
    const { user } = renderMenu()
    await user.click(screen.getByRole('button', { name: 'Color mode: System' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }))
    cleanup()

    renderMenu()

    expect(screen.getByRole('button', { name: 'Color mode: Dark' })).toBeInTheDocument()
    expect(themeColors()).toEqual([DARK_BACKGROUND, DARK_BACKGROUND])
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
