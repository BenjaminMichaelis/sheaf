import type { components } from '@/api/schema'

export type Capture = components['schemas']['CaptureResponse']

export type Proposal = components['schemas']['ProposalResponse']

export type ProposedPerson = components['schemas']['ProposedPerson']

export type PersonSummary = components['schemas']['PersonSummary']

// These mirror the C# enums, which the API sends as numbers.
export const CaptureKind = { QuickAdd: 0, Note: 1, Voice: 2 } as const

export const ProposalKind = { NewPerson: 0, MatchExisting: 1, MergeDuplicates: 2 } as const

export const ProposalStatus = { Pending: 0, Approved: 1, Rejected: 2 } as const

const sourceLabels = ['Added by a leader', 'Planning Center', 'Church member']

export function sourceLabel(source: PersonSummary['source']) {
  return source === null ? null : (sourceLabels[Number(source)] ?? null)
}

/** Notes for the demo script (docs/demo-script.md): Maya again, a new freshman, a likely duplicate, a sensitive detail and a minor. */
export const SAMPLE_NOTES = [
  {
    label: 'Maya at the fair',
    text: "Met Maya Chen at the activities fair. She's a freshman in Alder Hall and loves volleyball. Her number is (312) 555-0420. Invite her to Thursday women's study.",
  },
  {
    label: 'Welcome week BBQ',
    text: "Met Marcus Lee at the welcome week BBQ. He's a freshman in Smith Hall and plays bass. His number is (773) 555-0342. Invite him to Tuesday guys' group.",
  },
  {
    label: 'Someone you may know',
    text: 'Talked with Chris Nguyen again after the Sunday service. He wants to come to guys group on Tuesday and asked about a ride.',
  },
  {
    label: 'Something sensitive',
    text: 'Met Jordan Ellis at the activities fair, a sophomore in Birch Hall who loves photography. She mentioned her mom was just diagnosed with cancer. Grab coffee with her next week.',
  },
  {
    label: 'A high schooler',
    text: "Met Ava Turner at Sunday service. She's 16 and in high school, her mom drove her. She's interested in the worship team.",
  },
]
