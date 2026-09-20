import { describe, expect, it } from "vitest";
import { returnPathIn, withReturnTo } from "./returnTo";

describe("returnPathIn", () => {
  it("goes to the dashboard when nothing was asked for", () => {
    expect(returnPathIn(new URLSearchParams())).toBe("/");
  });

  it("honours an in-application path, query included", () => {
    const params = new URLSearchParams({ next: "/orgs/acme/members?tab=1" });

    expect(returnPathIn(params)).toBe("/orgs/acme/members?tab=1");
  });

  it("refuses an absolute URL", () => {
    expect(returnPathIn(new URLSearchParams({ next: "https://evil.test/x" }))).toBe("/");
  });

  it("refuses a protocol-relative URL", () => {
    expect(returnPathIn(new URLSearchParams({ next: "//evil.test" }))).toBe("/");
  });

  it("refuses a backslash standing in for the second slash", () => {
    expect(returnPathIn(new URLSearchParams({ next: "/\\evil.test" }))).toBe("/");
  });
});

describe("withReturnTo", () => {
  it("leaves the path alone from the dashboard", () => {
    expect(withReturnTo("/login", { pathname: "/", search: "" })).toBe("/login");
  });

  it("carries where we are, query included", () => {
    expect(withReturnTo("/login", { pathname: "/orgs/a", search: "?x=1" })).toBe(
      "/login?next=" + encodeURIComponent("/orgs/a?x=1"),
    );
  });
});
