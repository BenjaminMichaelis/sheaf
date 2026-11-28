import { useCallback, useEffect, useRef, useState } from 'react'

interface RecognitionResult {
  isFinal: boolean
  0: { transcript: string }
}

interface RecognitionEvent {
  resultIndex: number
  results: ArrayLike<RecognitionResult>
}

interface Recognition {
  continuous: boolean
  interimResults: boolean
  lang: string
  onresult: ((event: RecognitionEvent) => void) | null
  onend: (() => void) | null
  onerror: (() => void) | null
  start: () => void
  stop: () => void
}

type RecognitionConstructor = new () => Recognition

// Not every browser has speech recognition, and the prefixed name is not in TypeScript's DOM types.
declare global {
  interface Window {
    SpeechRecognition?: RecognitionConstructor
    webkitSpeechRecognition?: RecognitionConstructor
  }
}

function recognitionConstructor(): RecognitionConstructor | null {
  return window.SpeechRecognition ?? window.webkitSpeechRecognition ?? null
}

/**
 * Dictation with the browser's built-in speech recognition. Final phrases are appended through onText; the live,
 * unfinished phrase is returned as interim so the page can show it.
 */
export function useDictation(onText: (text: string) => void) {
  const supported = recognitionConstructor() !== null
  const [listening, setListening] = useState(false)
  const [interim, setInterim] = useState('')
  const recognition = useRef<Recognition | null>(null)
  const onTextRef = useRef(onText)

  useEffect(() => {
    onTextRef.current = onText
  }, [onText])

  useEffect(() => () => recognition.current?.stop(), [])

  const start = useCallback(() => {
    const Constructor = recognitionConstructor()

    if (!Constructor) return

    const instance = new Constructor()

    instance.continuous = true
    instance.interimResults = true
    instance.lang = 'en-US'
    instance.onresult = event => {
      let pending = ''

      for (let i = event.resultIndex; i < event.results.length; i++) {
        const result = event.results[i]

        if (!result) continue

        if (result.isFinal) onTextRef.current(result[0].transcript.trim())
        else pending += result[0].transcript
      }

      setInterim(pending)
    }

    instance.onend = () => {
      setListening(false)
      setInterim('')
    }

    instance.onerror = () => setListening(false)

    recognition.current = instance
    instance.start()
    setListening(true)
  }, [])

  const stop = useCallback(() => recognition.current?.stop(), [])

  return { supported, listening, interim, start, stop }
}
