import { useEffect, type ReactNode } from 'react'
import { CssBaseline } from '@mui/material'
import { ThemeProvider, useColorScheme } from '@mui/material/styles'
import { theme } from '@/theme'

/**
 * Applies the app theme. MUI keeps the chosen mode (light, dark or system, the default) in localStorage and in sync
 * across tabs. `noSsr` reads it on the first render, which suits this client-only app and avoids a light flash.
 */
export default function AppThemeProvider({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider theme={theme} noSsr disableTransitionOnChange>
      {/* enableColorScheme sets CSS color-scheme, so native scrollbars and form controls follow the mode. */}
      <CssBaseline enableColorScheme />
      <ThemeColorMeta />
      {children}
    </ThemeProvider>
  )
}

/**
 * Keeps the browser and OS chrome (`<meta name="theme-color">`) matching the page background in the current mode.
 * index.html has one tag per system scheme for the first paint; once the mode is known, both get its color, so a
 * mode pinned against the system setting still matches.
 */
function ThemeColorMeta() {
  const { colorScheme } = useColorScheme()
  const color = colorScheme ? theme.colorSchemes[colorScheme]?.palette.background.default : undefined

  useEffect(() => {
    if (color) {
      document.querySelectorAll('meta[name="theme-color"]').forEach(meta => meta.setAttribute('content', color))
    }
  }, [color])

  return null
}
