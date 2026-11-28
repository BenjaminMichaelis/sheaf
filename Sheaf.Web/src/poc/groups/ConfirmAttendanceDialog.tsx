import { useState } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Radio,
  RadioGroup,
  Stack,
  Typography,
} from '@mui/material'
import { formatDay, type Confirmation, type ConfirmationChoice } from './groupData'

const NEW = 'new'

/** Asks before saving a second meeting on the same day, or a new person who may already be in the church. */
export default function ConfirmAttendanceDialog({
  confirmation,
  occurredOn,
  onClose,
}: {
  confirmation: Confirmation
  occurredOn: string
  onClose: (choice: ConfirmationChoice) => void
}) {
  // Default each possible duplicate to its first match: re-adding someone already here is the usual mistake.
  const [picks, setPicks] = useState<Record<number, string>>(() =>
    Object.fromEntries(confirmation.similarPeople.map(similar => [similar.index, similar.matches[0]?.id ?? NEW])),
  )

  const existingSessionId = confirmation.existingSessionIds[0]

  const handleSave = () => {
    const matches = new Map<number, { id: string; name: string }>()

    for (const similar of confirmation.similarPeople) {
      const match = similar.matches.find(m => m.id === picks[similar.index])

      if (match) matches.set(similar.index, { id: match.id, name: match.name })
    }

    onClose({ kind: 'save', matches })
  }

  return (
    <Dialog open onClose={() => onClose({ kind: 'cancel' })} fullWidth maxWidth="xs">
      <DialogTitle>Quick check</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5}>
          {existingSessionId && (
            <Stack spacing={1}>
              <Typography>
                A meeting on {formatDay(occurredOn, { weekday: 'long', month: 'short', day: 'numeric' })} is already recorded.
              </Typography>
              <Button variant="outlined" onClick={() => onClose({ kind: 'open-existing', sessionId: existingSessionId })}>
                Edit that meeting instead
              </Button>
            </Stack>
          )}
          {confirmation.similarPeople.map(similar => {
            const person = confirmation.newPeople[similar.index]

            if (!person) return null

            return (
              <Stack key={similar.index} spacing={0.5}>
                <Typography>
                  Is <strong>{`${person.firstName} ${person.lastName}`.trim()}</strong> someone already here?
                </Typography>
                <RadioGroup value={picks[similar.index] ?? NEW} onChange={event => setPicks(previous => ({ ...previous, [similar.index]: event.target.value }))}>
                  {similar.matches.map(match => (
                    <FormControlLabel
                      key={match.id}
                      value={match.id}
                      control={<Radio />}
                      label={match.householdLabel ? `${match.name} (${match.householdLabel})` : match.name}
                    />
                  ))}
                  <FormControlLabel value={NEW} control={<Radio />} label="No, add a new person" />
                </RadioGroup>
              </Stack>
            )
          })}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => onClose({ kind: 'cancel' })}>Cancel</Button>
        <Button variant="contained" onClick={handleSave}>
          {existingSessionId ? 'Save as another meeting' : 'Save'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
