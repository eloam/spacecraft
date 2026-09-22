import { afterEach, describe, expect, it, vi } from "vitest";
import { formatDate, formatElapsed, formatSeconds, plural, timeAgo } from "./format";

// The formatters follow the reader's locale, so the assertions read the digits and the
// order of the units rather than the words between them.
describe("formatSeconds", () => {
  it("rounds to whole seconds and never goes negative", () => {
    expect(formatSeconds(4.6)).toMatch(/^5\D*$/);
    expect(formatSeconds(-3)).toMatch(/^0\D*$/);
  });

  it("says 0s for nothing rather than nothing at all", () => {
    expect(formatSeconds(0)).toMatch(/0/);
  });

  it("drops the units that are zero", () => {
    expect(formatSeconds(60)).toMatch(/^1\D*$/);
    expect(formatSeconds(3600)).toMatch(/^1\D*$/);
  });

  it("counts hours instead of letting the minutes run past sixty", () => {
    expect(formatSeconds(3912)).toMatch(/^1\D+5\D+12\D*$/);
  });
});

describe("formatElapsed", () => {
  it("measures from the first instant to the second", () => {
    expect(formatElapsed("2026-09-20T10:00:00Z", "2026-09-20T10:01:30Z")).toMatch(/^1\D+30\D*$/);
  });
});

describe("plural", () => {
  it("adds an s except for exactly one", () => {
    expect(plural(0, "project")).toBe("0 projects");
    expect(plural(1, "project")).toBe("1 project");
    expect(plural(2, "project")).toBe("2 projects");
  });
});

describe("timeAgo", () => {
  afterEach(() => vi.useRealTimers());

  it("falls back to the date past a year", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-20T12:00:00Z"));

    const iso = "2025-03-01T12:00:00Z";
    expect(timeAgo(iso)).toBe(formatDate(iso));
  });
});
