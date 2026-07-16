// LEARN: the client MIRRORS the server's rules; the server DECIDES. This validation exists purely
// for fast, kind feedback (don't make a shopper round-trip to learn their password is too short) —
// it is NOT a security boundary. The API re-checks every rule regardless, because anything the
// browser enforces, an attacker can skip. Keep these in step with the server: MIN_PASSWORD_LENGTH
// mirrors RegisterCustomer.MinPasswordLength, the email shape mirrors EmailAddress.From.
export const MIN_PASSWORD_LENGTH = 10;

export function looksLikeEmail(value: string): boolean {
  const at = value.indexOf('@');
  return at > 0 && at === value.lastIndexOf('@') && at < value.length - 1 && !value.includes(' ');
}

/** A user-facing message for the first broken rule, or null when the form is submittable. */
export function validateRegistration(email: string, password: string): string | null {
  if (!looksLikeEmail(email)) {
    return 'Enter a valid email address.';
  }
  if (password.length < MIN_PASSWORD_LENGTH) {
    return `Password must be at least ${MIN_PASSWORD_LENGTH} characters.`;
  }
  return null;
}
