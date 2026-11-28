import { useEffect, useState } from 'react'
import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { onChurchConflict } from '@/services/apiClient'

/**
 * Explains a refused request when this page still acts for a church the account no longer has selected
 * (usually after switching church in another tab), and offers to reload into the selected church.
 */
export default function ChurchConflictDialog({ reload = () => window.location.reload() }: { reload?: () => void }) {
  const [open, setOpen] = useState(false)

  useEffect(() => onChurchConflict(() => setOpen(true)), [])

  return (
    // MUI names the dialog by its DialogTitle.
    <Dialog open={open} onClose={() => setOpen(false)}>
      <DialogTitle>Your church changed</DialogTitle>
      <DialogContent>
        <DialogContentText>
          This page was opened for a different church than the one you now have selected, perhaps in another tab.
          Nothing was loaded or saved for the wrong church. Reload to continue in the selected church.
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setOpen(false)}>Not now</Button>
        <Button variant="contained" onClick={reload}>
          Reload
        </Button>
      </DialogActions>
    </Dialog>
  )
}
