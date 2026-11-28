import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Autocomplete, Box, CircularProgress, TextField, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { LOOKUP_MIN_LENGTH, peopleLookupQuery } from '@/services/churchQueries'
import { personHint } from '@/utils/names'

type PersonMatch = components['schemas']['PersonLookupResponse']

/** The last option in the list, which creates a new person with the typed name instead of choosing one. */
interface CreateOption {
  create: string
}

type Option = PersonMatch | CreateOption

const SEARCH_DELAY_MS = 300

interface PersonSearchProps {
  churchId: string
  label: string
  onPick: (person: PersonMatch) => void
  onCreate: (name: string) => void
}

/**
 * One name box that searches the church's active people as you type. The list ends with "Create new person", as MUI's
 * creatable example does, so someone not found can be added without leaving the box. Results show only what tells
 * similar names apart.
 */
export default function PersonSearch({ churchId, label, onPick, onCreate }: PersonSearchProps) {
  const [input, setInput] = useState('')
  const typed = input.trim()
  const name = useDebouncedValue(typed, SEARCH_DELAY_MS)
  const lookup = useQuery(peopleLookupQuery(churchId, name))

  // Only results for exactly what is typed are offered, so a match for an earlier name can never be picked.
  const searching = typed.length >= LOOKUP_MIN_LENGTH && (name !== typed || lookup.isFetching)
  const matches = typed.length >= LOOKUP_MIN_LENGTH && name === typed ? (lookup.data ?? []) : []
  const options: Option[] = typed ? [...matches, { create: typed }] : []

  return (
    <Autocomplete<Option>
      options={options}
      value={null}
      inputValue={input}
      // The first option is highlighted, so Enter always chooses something; typed text survives closing the keyboard.
      autoHighlight
      clearOnBlur={false}
      onInputChange={(_, value, reason) => {
        if (reason === 'input' || reason === 'clear') setInput(value)
      }}
      onChange={(_, option) => {
        if (option === null) return

        if ('create' in option) onCreate(option.create)
        else onPick(option)
        // The box empties for the next name rather than showing the choice.
        setInput('')
      }}
      // The API already filtered by name, so MUI's own filtering is turned off (mui.com/material-ui/react-autocomplete/#search-as-you-type).
      filterOptions={all => all}
      getOptionLabel={option => ('create' in option ? option.create : option.name)}
      getOptionKey={optionKey}
      // Create waits for the search, so a quick Enter can't create someone who is about to be found.
      getOptionDisabled={option => 'create' in option && searching}
      renderOption={({ key, ...props }, option) => (
        <Box component="li" key={key} {...props}>
          {'create' in option ? (
            <Typography color="primary">Create new person “{option.create}”</Typography>
          ) : (
            <Box>
              <Typography>{option.name}</Typography>
              <Typography variant="body2" color="text.secondary">
                {personHint(option)}
              </Typography>
            </Box>
          )}
        </Box>
      )}
      renderInput={params => (
        <TextField
          {...params}
          label={label}
          // The create option is always listed, so errors are shown here rather than as MUI's empty-list text.
          error={lookup.isError}
          helperText={lookup.isError ? `Search failed: ${lookup.error.message}` : undefined}
          // As MUI's asynchronous example does, the spinner joins Autocomplete's own adornment and props.
          slotProps={{
            ...params.slotProps,
            input: {
              ...params.slotProps.input,
              endAdornment: (
                <>
                  {searching && <CircularProgress color="inherit" size={20} aria-label="Searching" />}
                  {params.slotProps.input.endAdornment}
                </>
              ),
            },
          }}
        />
      )}
    />
  )
}

function optionKey(option: Option) {
  return 'create' in option ? 'create' : option.id
}
