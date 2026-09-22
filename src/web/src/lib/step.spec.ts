import { describe, expect, it } from "vitest";
import { wrapStep } from "./step";

describe("wrapStep", () => {
  it("starts at the first item going forwards and the last going backwards", () => {
    expect(wrapStep(-1, 1, 5)).toBe(0);
    expect(wrapStep(-1, -1, 5)).toBe(4);
  });

  it("moves one item either way", () => {
    expect(wrapStep(2, 1, 5)).toBe(3);
    expect(wrapStep(2, -1, 5)).toBe(1);
  });

  it("wraps past both ends", () => {
    expect(wrapStep(4, 1, 5)).toBe(0);
    expect(wrapStep(0, -1, 5)).toBe(4);
  });

  it("stays put on the only item", () => {
    expect(wrapStep(0, 1, 1)).toBe(0);
    expect(wrapStep(0, -1, 1)).toBe(0);
  });

  it("has nowhere to go with nothing to step through", () => {
    expect(wrapStep(-1, 1, 0)).toBeNull();
    expect(wrapStep(-1, -1, 0)).toBeNull();
  });
});
