import { cn } from "@/lib/utils";

/**
 * Each language's own colour, as its ecosystem uses it — the TypeScript blue, the Go cyan,
 * the Python yellow — and the lettermark it is known by on it. Hard-coded rather than themed:
 * these are borrowed identities, and a palette that shifted them to match the app would stop
 * them being recognisable, which is the only thing they are for. Whole class names, because
 * Tailwind only compiles the ones it can read as written.
 *
 * Drawn rather than imported: these are two glyphs on a rounded square, which is what the
 * real logos are, and a brand-icon dependency would have been 500 marks to ship the one this
 * app can generate today. Python's yellow is the exception that needs dark letters — at 0.86
 * lightness, white on it is unreadable.
 */
const languages: Record<number, { short: string; className: string }> = {
  1: { short: "TS", className: "bg-[#3178c6] text-white" },
  2: { short: "C#", className: "bg-[#8a51a5] text-white" },
  3: { short: "Jv", className: "bg-[#e76f00] text-white" },
  4: { short: "Py", className: "bg-[#ffd43b] text-black" },
  5: { short: "PHP", className: "bg-[#787cb5] text-white" },
  6: { short: "Go", className: "bg-[#00add8] text-white" },
};

/**
 * The square that stands in for a language where there is room for it — a table row, a card.
 * Where there is not, {@link LanguageDot} carries the same colour inline.
 */
export function LanguageMark({ language, className }: { language: number; className?: string }) {
  const mark = languages[language];
  return (
    <div
      className={cn(
        "flex size-10 shrink-0 items-center justify-center rounded-lg text-xs font-semibold",
        mark ? mark.className : "bg-muted text-muted-foreground",
        className,
      )}
      // Decorative: the language is named in full beside it every time.
      aria-hidden
    >
      {mark ? mark.short : "?"}
    </div>
  );
}

/**
 * The dot that precedes a language's name, wherever one is shown.
 *
 * Decorative, so it carries aria-hidden: the name is always beside it, and a screen reader
 * announcing a colour would add nothing a sighted reader gets from the word itself.
 */
export function LanguageDot({ language, className }: { language: number; className?: string }) {
  return (
    <span
      className={cn(
        "size-2 shrink-0 rounded-full",
        // The text colour rides along and paints nothing: the dot has no text.
        languages[language]?.className ?? "bg-muted-foreground",
        className,
      )}
      aria-hidden
    />
  );
}
