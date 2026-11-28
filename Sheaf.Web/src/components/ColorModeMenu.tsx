import { useId, useState, type ReactNode } from 'react'
import { IconButton, ListItemIcon, ListItemText, Menu, MenuItem } from '@mui/material'
import { useColorScheme } from '@mui/material/styles'
import { DarkMode, LightMode, SettingsBrightness } from '@mui/icons-material'

type Mode = 'system' | 'light' | 'dark'

interface ModeOption {
  mode: Mode
  label: string
  icon: ReactNode
}

const SYSTEM: ModeOption = { mode: 'system', label: 'System', icon: <SettingsBrightness /> }

const MODES: ModeOption[] = [
  SYSTEM,
  { mode: 'light', label: 'Light', icon: <LightMode /> },
  { mode: 'dark', label: 'Dark', icon: <DarkMode /> },
]

/** Lets the user follow the system color scheme or pin light or dark. */
export default function ColorModeMenu() {
  const { mode, setMode } = useColorScheme()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const menuId = useId()
  const current = MODES.find(option => option.mode === mode) ?? SYSTEM

  const choose = (next: Mode) => {
    setMode(next)
    setAnchor(null)
  }

  return (
    <>
      <IconButton
        color="inherit"
        onClick={event => setAnchor(event.currentTarget)}
        aria-label={`Color mode: ${current.label}`}
        aria-haspopup="menu"
        aria-controls={anchor ? menuId : undefined}
        aria-expanded={anchor ? 'true' : undefined}
      >
        {current.icon}
      </IconButton>
      <Menu id={menuId} anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {MODES.map(option => (
          // menuitemradio makes MUI announce the selected option as checked.
          <MenuItem
            key={option.mode}
            role="menuitemradio"
            selected={option.mode === mode}
            onClick={() => choose(option.mode)}
          >
            <ListItemIcon>{option.icon}</ListItemIcon>
            <ListItemText>{option.label}</ListItemText>
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
