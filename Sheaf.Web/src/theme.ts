import { createTheme } from '@mui/material/styles'
import type {} from '@mui/material/themeCssVarsAugmentation'
import LinkBehavior from '@/components/LinkBehavior'

/**
 * The app theme. Light and dark are both color schemes of one theme, emitted as CSS variables, so switching mode only
 * swaps a class on <html> instead of re-rendering the tree (mui.com/material-ui/customization/css-theme-variables).
 * Style dark-only differences with `theme.applyStyles('dark', …)`, not `theme.palette.mode`, which flickers.
 * Use MUI's `Link` (or `href` on a button) for in-app links so they navigate in the app and use the theme's colors.
 * They render react-router links, so they must sit inside the Router, and same-origin links that need a full page
 * load (such as a backend redirect under /api) need a plain <a>.
 */
export const theme = createTheme({
  cssVariables: { colorSchemeSelector: 'class' },
  colorSchemes: { light: true, dark: true },
  // MUI defaults to Roboto, which would have to be downloaded; each device's own UI font needs nothing loaded, works
  // offline, and looks native on phones (mui.com/material-ui/customization/typography/#font-family).
  typography: {
    fontFamily: [
      '-apple-system',
      'BlinkMacSystemFont',
      '"Segoe UI"',
      'Roboto',
      'Oxygen',
      'Ubuntu',
      'Cantarell',
      '"Fira Sans"',
      '"Droid Sans"',
      '"Helvetica Neue"',
      'Arial',
      'sans-serif',
      '"Apple Color Emoji"',
      '"Segoe UI Emoji"',
    ].join(','),
  },
  components: {
    MuiLink: { defaultProps: { component: LinkBehavior } },
    MuiButtonBase: { defaultProps: { LinkComponent: LinkBehavior } },
  },
})
