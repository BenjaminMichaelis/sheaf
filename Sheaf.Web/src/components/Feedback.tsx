import { Box, Button, CircularProgress } from '@mui/material'

/** An alert's action button, styled as MUI's Alert examples show. */
export function RetryButton({ onClick }: { onClick: () => void }) {
  return (
    <Button color="inherit" size="small" onClick={onClick}>
      Retry
    </Button>
  )
}

/** A centred spinner for content that is still loading. */
export function Loading() {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
      <CircularProgress aria-label="Loading" />
    </Box>
  )
}
