import { useId, useState, type ReactNode } from 'react'
import { Checkbox, FormControlLabel, IconButton, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import { Close as CloseIcon } from '@mui/icons-material'
import NewPersonDialog from '@/components/NewPersonDialog'
import PersonSearch from '@/components/PersonSearch'
import { fullName } from '@/utils/names'
import type { Attendees, Named } from './useAttendees'

interface AttendeesEditorProps {
  churchId: string
  attendees: Attendees
  /** The people to tick, such as the roster for the meeting date. */
  listed: readonly Named[]
  /** The heading over the ticked list, which also names it. */
  heading: string
  /** Shown under the heading in place of, or above, the list: loading, an error, or a note that it is empty. */
  status?: ReactNode
}

/**
 * Marks who came: a checkbox for each listed person, then a name search for anyone else, ending with "Create new
 * person". Used both to record a meeting and to correct one.
 */
export default function AttendeesEditor({ churchId, attendees, listed, heading, status }: AttendeesEditorProps) {
  const listedHeading = useId()
  const othersHeading = useId()
  const [creating, setCreating] = useState({ open: false, name: '' })

  return (
    <>
      <section aria-labelledby={listedHeading}>
        <Typography id={listedHeading} variant="h6" component="h2">
          {heading}
        </Typography>
        {status}
        {listed.length > 0 && (
          <Paper variant="outlined">
            <List disablePadding aria-labelledby={listedHeading}>
              {listed.map(person => (
                <ListItem key={person.id} divider sx={{ py: 0 }}>
                  {/* One focusable checkbox per person, named by its visible label (mui.com/material-ui/react-checkbox/#label). */}
                  <FormControlLabel
                    control={<Checkbox checked={attendees.present.has(person.id)} onChange={() => attendees.toggle(person.id)} />}
                    label={person.name}
                    sx={{ flex: 1, mr: 0, py: 0.5 }}
                  />
                </ListItem>
              ))}
            </List>
          </Paper>
        )}
      </section>

      <section aria-labelledby={othersHeading}>
        <Typography id={othersHeading} variant="h6" component="h2" gutterBottom>
          Also came
        </Typography>
        <PersonSearch
          churchId={churchId}
          label="Find or add a person"
          onPick={attendees.markPresent}
          onCreate={name => setCreating({ open: true, name })}
        />
        {(attendees.others.length > 0 || attendees.newPeople.length > 0) && (
          <List aria-labelledby={othersHeading}>
            {attendees.others.map(person => (
              <RemovableItem key={person.id} primary={person.name} onRemove={() => attendees.removeFound(person.id)} />
            ))}
            {attendees.newPeople.map(person => (
              <RemovableItem
                key={`new-${person.key}`}
                primary={fullName(person.request)}
                secondary="New person"
                onRemove={() => attendees.removeNewPerson(person.key)}
              />
            ))}
          </List>
        )}
      </section>

      <NewPersonDialog
        open={creating.open}
        initialName={creating.name}
        onClose={() => setCreating(previous => ({ ...previous, open: false }))}
        onAdd={person => {
          attendees.addNewPerson(person)
          setCreating(previous => ({ ...previous, open: false }))
        }}
      />
    </>
  )
}

function RemovableItem({ primary, secondary, onRemove }: { primary: string; secondary?: string; onRemove: () => void }) {
  return (
    <ListItem
      secondaryAction={
        <IconButton edge="end" onClick={onRemove} aria-label={`Remove ${primary}`}>
          <CloseIcon />
        </IconButton>
      }
    >
      <ListItemText primary={primary} secondary={secondary} />
    </ListItem>
  )
}
