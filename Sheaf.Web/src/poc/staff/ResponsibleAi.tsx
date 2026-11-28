import { Box, Card, CardContent, List, ListItem, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material'
import { Block, CheckCircle } from '@mui/icons-material'
import { usePocStatus } from '../usePocStatus'

const does = [
  { title: 'Reads notes you choose to send', detail: 'When you tap "Read my note", the note goes to Claude to pull out names, details and a next step.' },
  { title: 'Suggests, never decides', detail: 'Everything AI produces is a suggestion. Nothing is saved until a person approves it, and you can edit it first.' },
  { title: 'Drafts texts you send yourself', detail: 'Drafts are labeled AI-drafted. You edit them and send from your own phone; Sheaf never messages anyone.' },
  { title: 'Writes a weekly brief from totals', detail: 'The staff brief is written from counts and group names only. No student names, notes or contact details are sent.' },
]

const never = [
  { title: 'Never sees sensitive details', detail: 'Sentences about health, mental health, abuse, addiction, legal trouble, grief, sexuality or money are removed before AI sees a note. A pastor sees the original.' },
  { title: 'Never drafts for minors', detail: 'No AI drafts, no phone or email stored for anyone under 18. Contact goes through a parent or guardian.' },
  { title: "Never contacts someone who asked not to be", detail: "Don't contact stops drafts and nudges for every leader." },
  { title: 'Never scores people', detail: 'No rankings of students or leaders. Dashboards show where follow-up needs help, as counts.' },
  { title: 'Never trains on your data', detail: 'Notes go to the Claude API, which does not train on API data by default. Sheaf keeps the redacted note and its audit trail.' },
]

/** What the AI in Sheaf does and never does, in plain language for leaders, staff and the people they care for. */
export default function ResponsibleAi() {
  const status = usePocStatus()

  return (
    <Stack spacing={2.5}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          How Sheaf uses AI
        </Typography>
        <Typography color="text.secondary">
          AI helps leaders remember people and follow up. People make every decision.{' '}
          {status && (status.aiLive ? 'Right now notes go to Claude (Anthropic).' : 'Right now a built-in fallback stands in for Claude, so nothing leaves this server.')}
        </Typography>
      </Box>

      <Section title="What it does" items={does} icon={<CheckCircle color="success" />} />
      <Section title="What it never does" items={never} icon={<Block color="error" />} />

      <Card variant="outlined">
        <CardContent>
          <Typography variant="overline" color="text.secondary">
            Where data goes
          </Typography>
          <Typography variant="body2">
            Notes are redacted on the Sheaf server, then only the redacted text goes to the Claude API. The original of a redacted note is kept
            for staff in the pastor inbox. Every AI suggestion, approval, edit and draft is written to the audit log that staff can review.
          </Typography>
        </CardContent>
      </Card>
    </Stack>
  )
}

function Section({ title, items, icon }: { title: string; items: { title: string; detail: string }[]; icon: React.ReactNode }) {
  return (
    <Box>
      <Typography variant="overline" color="text.secondary">
        {title}
      </Typography>
      <Card variant="outlined">
        <List disablePadding>
          {items.map(item => (
            <ListItem key={item.title} divider alignItems="flex-start">
              <ListItemIcon sx={{ minWidth: 36, mt: 0.5 }}>{icon}</ListItemIcon>
              <ListItemText primary={item.title} secondary={item.detail} />
            </ListItem>
          ))}
        </List>
      </Card>
    </Box>
  )
}
