import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

const THEMES = ["light", "dark", "system"] as const;
type Theme = (typeof THEMES)[number];

type ThemeProviderState = {
  theme: Theme;
  setTheme: (theme: Theme) => void;
};

const ThemeProviderContext = createContext<ThemeProviderState>({
  theme: "system",
  setTheme: () => null,
});

function isTheme(value: string | null): value is Theme {
  return THEMES.includes(value as Theme);
}

/**
 * The one place the theme is decided, shadcn's "Dark mode — Vite" provider.
 *
 * It toggles the `dark` class the shadcn variables key off: `@custom-variant dark
 * (&:is(.dark *))` only reacts to the class, never to the media query on its own. Before
 * this, every component that wanted the class applied called a hook that kept its own copy
 * of the choice, and the toaster read a provider from another library that was never
 * mounted — so a forced dark theme on a light desktop got light toasts.
 */
export function ThemeProvider({
  children,
  defaultTheme = "system",
  storageKey = "spacecraft.theme",
}: {
  children: ReactNode;
  defaultTheme?: Theme;
  storageKey?: string;
}) {
  // Read through a type guard: the key is in localStorage, which anything on this origin can
  // write, and a value like "Dark" would otherwise be applied as if it were one of ours.
  const [theme, setTheme] = useState<Theme>(() => {
    const stored = localStorage.getItem(storageKey);
    return isTheme(stored) ? stored : defaultTheme;
  });

  useEffect(() => {
    const root = window.document.documentElement;
    const media = window.matchMedia("(prefers-color-scheme: dark)");

    const apply = () => {
      const resolved = theme === "system" ? (media.matches ? "dark" : "light") : theme;
      root.classList.toggle("dark", resolved === "dark");
    };

    apply();

    // Follow the OS while on "system": the guide applies the preference once, at mount, and
    // a laptop that switches to dark at dusk would otherwise keep a light page until reload.
    if (theme !== "system") return;
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [theme]);

  const value: ThemeProviderState = {
    theme,
    setTheme: (next) => {
      localStorage.setItem(storageKey, next);
      setTheme(next);
    },
  };

  return <ThemeProviderContext.Provider value={value}>{children}</ThemeProviderContext.Provider>;
}

// The guide's shape: the hook beside its provider. Fast refresh remounts this file's tree
// when it changes, which for a provider of one value is no loss.
// oxlint-disable-next-line react/only-export-components
export function useTheme() {
  return useContext(ThemeProviderContext);
}
