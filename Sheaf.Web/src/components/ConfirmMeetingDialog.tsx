import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Typography,
} from '@mui/material'
import type { components } from '@/api/schema'
import { personHint } from '@/utils/names'

type PersonMatch = components['schemas']['PersonLookupResponse']

/** A new person on the meeting whose name matches people who may already exist. */
export interface PossibleDuplicate {
  key: number
  name: string
  matches: PersonMatch[]
}

interface ConfirmMeetingDialogProps {
  open: boolean
  /** The church's word for a group, e.g. "village". */
  groupLabel: string
  /** True when the group already has a meeting on this date. */
  sameDay: boolean
  duplicates: PossibleDuplicate[]
  saving: boolean
  onUseExisting: (duplicate: PossibleDuplicate, person: PersonMatch) => void
  onSaveAnyway: () => void
  onClose: () => void
}

/**
 * Shown when the API asks to confirm a save: another meeting that day, or new people who may already exist. Each match
 * can be chosen instead of creating someone new (#188 UI-DUP-02); nothing is ever merged.
 */
export default function ConfirmMeetingDialog({ open, groupLabel, sameDay, duplicates, saving, onUseExisting, onSaveAnyway, onClose }: ConfirmMeetingDialogProps) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Check before saving</DialogTitle>
      <DialogContent>
        {sameDay && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            A meeting is already recorded for this date. Save another one only if the {groupLabel} met twice.
          </Alert>
        )}
        {duplicates.map(duplicate => (
          <section key={duplicate.key} aria-labelledby={`duplicate-${duplicate.key}`}>
            <Typography id={`duplicate-${duplicate.key}`} variant="subtitle1" sx={{ mt: 1 }}>
              “{duplicate.name}” may already be here
            </Typography>
            <List dense aria-labelledby={`duplicate-${duplicate.key}`}>
              {duplicate.matches.map(match => (
                <ListItem
                  key={match.id}
                  secondaryAction={
                    // The accessible name starts with the visible text, so voice control users can say what they see.
                    <Button size="small" onClick={() => onUseExisting(duplicate, match)} aria-label={`Use this person: ${match.name}`}>
                      Use this person
                    </Button>
                  }
                >
                  <ListItemText primary={match.name} secondary={personHint(match)} />
                </ListItem>
              ))}
            </List>
          </section>
        ))}
        {duplicates.length > 0 && (
          <DialogContentText variant="body2">Choose a match to mark them present, or save to create new people.</DialogContentText>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Go back</Button>
        <Button onClick={onSaveAnyway} loading={saving} variant="contained">
          Save anyway
        </Button>
      </DialogActions>
    </Dialog>
  )
}
