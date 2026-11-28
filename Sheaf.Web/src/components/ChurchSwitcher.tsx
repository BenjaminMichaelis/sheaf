import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, ListItemIcon, Menu, MenuItem, Typography } from '@mui/material'
import { Check, ExpandMore } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { useChurch } from '@/contexts/ChurchContext'

/** Shows the church the user is acting in and, when they belong to several, lets them switch. */
export default function ChurchSwitcher() {
  const { church, churches, switchChurch } = useChurch()
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [switching, setSwitching] = useState(false)

  if (church === null) return null

  if (churches.length <= 1) {
    return (
      <Typography variant="subtitle1" component="span" noWrap sx={{ minWidth: 0 }}>
        {church.churchName}
      </Typography>
    )
  }

  const select = async (churchId: string) => {
    setAnchor(null)

    if (churchId === church.churchId) return
    setSwitching(true)

    try {
      await switchChurch(churchId)
      // The current page may not exist, or may not be allowed, in the new church; start again from home.
      navigate('/')
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not switch church.', { variant: 'error' })
    } finally {
      setSwitching(false)
    }
  }

  return (
    <>
      <Button
        color="inherit"
        endIcon={<ExpandMore />}
        onClick={event => setAnchor(event.currentTarget)}
        disabled={switching}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        aria-label={`${church.churchName}, switch church`}
        sx={{ textTransform: 'none', minWidth: 0 }}
      >
        <Typography variant="subtitle1" component="span" noWrap>
          {church.churchName}
        </Typography>
      </Button>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {churches.map(option => (
          <MenuItem
            key={option.churchId}
            selected={option.churchId === church.churchId}
            onClick={() => void select(option.churchId)}
          >
            <ListItemIcon>{option.churchId === church.churchId && <Check fontSize="small" />}</ListItemIcon>
            {option.churchName}
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
