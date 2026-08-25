import { useEffect, useState } from "react";

type Theme = "light" | "dark" | "system";

const STORAGE_KEY = "spacecraft.theme";

function resolve(theme: Theme): "light" | "dark" {
  if (theme !== "system") return theme;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * Applies the theme by toggling the `dark` class the shadcn variables key off.
 *
 * Without this the app is locked to light: shadcn's `@custom-variant dark (&:is(.dark *))`
 * only reacts to the class, never to the media query on its own.
 */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(
    () => (localStorage.getItem(STORAGE_KEY) as Theme | null) ?? "system",
  );

  useEffect(() => {
    const apply = () => {
      document.documentElement.classList.toggle("dark", resolve(theme) === "dark");
    };

    apply();
    localStorage.setItem(STORAGE_KEY, theme);

    if (theme !== "system") return;

    // Follow the OS while on "system".
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [theme]);

  return { theme, setTheme, resolved: resolve(theme) };
}
