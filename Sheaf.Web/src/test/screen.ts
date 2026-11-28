import mediaQuery from 'css-mediaquery'

/** Phone width: tests get the phone layout unless they ask for another width. */
export const PHONE_WIDTH = 375

/**
 * jsdom has no matchMedia. This answers media queries for a screen `width` pixels wide, as MUI's useMediaQuery testing
 * guide recommends (mui.com/material-ui/react-use-media-query/#testing), so layouts can be tested at each breakpoint.
 * Queries about anything but size (such as prefers-color-scheme) don't match, so tests use light mode.
 */
export function setScreenWidth(width: number) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: (query: string) => ({
      matches: mediaQuery.match(query, { width }),
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
