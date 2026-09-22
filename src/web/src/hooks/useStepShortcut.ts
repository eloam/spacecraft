import { useEffect, useEffectEvent } from "react";

/**
 * F8 steps forwards and Shift+F8 backwards while `active` — the pair an editor walks its
 * problems with. On the window rather than the dialog: the shortcut belongs to the page while
 * the dialog is open, not to whichever element holds focus, so it still fires when focus has
 * fallen back to the body.
 *
 * `step` closes over the current position and is a new function every render. Read through
 * an Effect Event it is always the latest one, and the listener is registered once per
 * opening rather than torn down and put back on every keystroke.
 */
export function useStepShortcut(active: boolean, step: (by: 1 | -1) => void) {
  const onStep = useEffectEvent(step);

  useEffect(() => {
    if (!active) return;

    function onKey(event: KeyboardEvent) {
      if (event.key !== "F8") return;
      event.preventDefault();
      onStep(event.shiftKey ? -1 : 1);
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [active]);
}
