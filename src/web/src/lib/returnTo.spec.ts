import { describe, expect, it } from "vitest";
import { returnPathIn, withReturnTo } from "./returnTo";

const ORIGIN = "https://app.test";

/** As the query string would carry it: percent-encoded, decoded by URLSearchParams. */
function pathIn(next: string) {
  return returnPathIn(new URLSearchParams({ next }), ORIGIN);
}

describe("returnPathIn", () => {
  it("goes to the dashboard when nothing was asked for", () => {
    expect(returnPathIn(new URLSearchParams(), ORIGIN)).toBe("/");
  });

  it("honours an in-application path, query and fragment included", () => {
    expect(pathIn("/orgs/acme/members?tab=1#row-3")).toBe("/orgs/acme/members?tab=1#row-3");
  });

  it("refuses an absolute URL", () => {
    expect(pathIn("https://evil.test/x")).toBe("/");
  });

  it("refuses a protocol-relative URL", () => {
    expect(pathIn("//evil.test")).toBe("/");
  });

  it("refuses a backslash standing in for the second slash", () => {
    expect(pathIn("/\\evil.test")).toBe("/");
  });

  // The URL parser drops tabs and newlines before it reads the authority, so to the browser
  // these are "//evil.test" — and a link can carry them as %09 and %0A.
  it("refuses a tab or a newline hiding the second slash", () => {
    expect(pathIn("/\t/evil.test")).toBe("/");
    expect(pathIn("/\n/evil.test")).toBe("/");
  });

  it("refuses a scheme", () => {
    expect(pathIn("javascript:alert(1)")).toBe("/");
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
