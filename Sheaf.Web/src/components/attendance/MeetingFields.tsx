import { Box, Button, TextField, Typography } from '@mui/material'

/** Most characters a meeting description may have, matching the API. */
const MAX_DESCRIPTION_LENGTH = 2000

export function DescriptionField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <TextField
      label="Description (optional)"
      value={value}
      onChange={event => onChange(event.target.value)}
      multiline
      minRows={2}
      slotProps={{ htmlInput: { maxLength: MAX_DESCRIPTION_LENGTH } }}
    />
  )
}

interface SaveBarProps {
  label: string
  /** How many are marked present, announced as it changes. */
  count: number
  disabled: boolean
  onSave: () => void
  children?: React.ReactNode
}

/**
 * The meeting's save button and present count. A button, not a form submit: Enter in the name search must choose a
 * person, never save the meeting.
 */
export function SaveBar({ label, count, disabled, onSave, children }: SaveBarProps) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
      <Button variant="contained" onClick={onSave} disabled={disabled}>
        {label}
      </Button>
      {children}
      {/* A status region, so screen readers announce the count as people are marked. */}
      <Typography role="status" color="text.secondary">
        {count} present
      </Typography>
    </Box>
  )
}
