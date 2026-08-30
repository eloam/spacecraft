import { cn } from "@/lib/utils";

/**
 * Each language's own colour, as its ecosystem uses it — the TypeScript blue, the Go cyan,
 * the Python yellow. Hard-coded rather than themed: these are borrowed identities, and a
 * palette that shifted them to match the app would stop them being recognisable, which is
 * the only thing they are for.
 */
const languageColor: Record<number, string> = {
  1: "bg-[#3178c6]", // TypeScript
  2: "bg-[#8a51a5]", // C#
  3: "bg-[#e76f00]", // Java
  4: "bg-[#ffd43b]", // Python
  5: "bg-[#787cb5]", // PHP
  6: "bg-[#00add8]", // Go
};

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
        languageColor[language] ?? "bg-muted-foreground",
        className,
      )}
      aria-hidden
    />
  );
}
