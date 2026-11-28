import { useState, type FormEvent } from 'react'
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
import type { components } from '@/api/schema'
import { splitName } from '@/utils/names'

type PersonRequest = components['schemas']['PersonRequest']

interface NewPersonDialogProps {
  open: boolean
  /** The name typed into the search, which starts the form. */
  initialName: string
  onClose: () => void
  onAdd: (person: PersonRequest) => void
}

/**
 * Collects a new person's name, with email and phone clearly optional (#188 UI-PPL-01). Nothing is saved until the
 * meeting is. MUI unmounts the form once the dialog has closed, so each opening starts fresh.
 */
export default function NewPersonDialog({ open, initialName, onClose, onAdd }: NewPersonDialogProps) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <NewPersonForm initialName={initialName} onClose={onClose} onAdd={onAdd} />
    </Dialog>
  )
}

function NewPersonForm({ initialName, onClose, onAdd }: Omit<NewPersonDialogProps, 'open'>) {
  const [name, setName] = useState(initialName)
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onAdd({ ...splitName(name), email: email.trim() || null, phone: phone.trim() || null })
  }

  return (
    <form onSubmit={submit}>
      <DialogTitle>New person</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField label="Name" value={name} onChange={event => setName(event.target.value)} required autoFocus />
          <TextField label="Email (optional)" type="email" value={email} onChange={event => setEmail(event.target.value)} />
          <TextField label="Phone (optional)" type="tel" value={phone} onChange={event => setPhone(event.target.value)} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button type="submit" disabled={!name.trim()}>
          Add
        </Button>
      </DialogActions>
    </form>
  )
}
