/** Fewer digits than this is not a number anyone could dial, such as what is left of "1-800-FLOWERS". */
const MIN_DIALABLE_DIGITS = 7

/**
 * A tel: link for a phone number as people type it, or null when it can't be dialled as written. Phone numbers are free
 * text, so the number stops at the first letter or # , ; / (an extension such as "ext 2" or "#2", a second number, a note such as "(cell)", or a vanity
 * number), keeps only what RFC 3966 allows (digits, a leading + and the visual separators "-.()"), and drops separators
 * left trailing.
 */
export function telHref(phone: string): string | null {
  const number = phone
    .split(/[a-z#,;/]/i)[0]
    ?.replace(/[^\d+().-]/g, '')
    .replace(/[^\d]+$/, '')

  return number && (number.match(/\d/g)?.length ?? 0) >= MIN_DIALABLE_DIGITS ? `tel:${number}` : null
}
