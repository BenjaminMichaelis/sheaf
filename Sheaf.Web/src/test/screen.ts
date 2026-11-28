import mediaQuery from 'css-mediaquery'

/** Phone width: tests get the phone layout unless they ask for another width. */
export const PHONE_WIDTH = 375

/**
 * jsdom has no matchMedia. This answers media queries for a screen `width` pixels wide, as MUI's useMediaQuery testing
 * guide recommends (mui.com/material-ui/react-use-media-query/#testing), so layouts can be tested at each breakpoint.
 * The system color scheme is light unless a test passes 'dark', which answers `prefers-color-scheme` queries.
 */
export function setScreenWidth(width: number, colorScheme: 'light' | 'dark' = 'light') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: (query: string) => ({
      matches: mediaQuery.match(query, { width, 'prefers-color-scheme': colorScheme }),
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  })
}
