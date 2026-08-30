/**
 * The signed-in user as a grey circle with their initial.
 *
 * Falls back to the email address when no display name is set, since the name is optional at
 * registration and an empty circle would read as a loading state.
 */
export function AccountAvatar({
  displayName,
  email,
  className = "",
}: {
  displayName?: string | null;
  email?: string | null;
  className?: string;
}) {
  const source = displayName?.trim() || email?.trim() || "";

  // Array.from, not [0]: indexing a string walks UTF-16 code units, so a name starting with
  // an emoji or any character outside the BMP would yield half a surrogate pair.
  const initial = Array.from(source)[0]?.toLocaleUpperCase() ?? "?";

  return (
    <span
      aria-hidden
      className={
        "inline-flex size-6 shrink-0 select-none items-center justify-center rounded-full " +
        // A tint of the text colour rather than --muted: in the light theme --muted is
        // the page ground and a hair off the top bar, so a muted circle vanished into
        // both. This is relative to whatever it is placed on, in either theme.
        "bg-foreground/10 text-[11px] font-medium leading-none text-foreground " +
        className
      }
    >
      {initial}
    </span>
  );
}
