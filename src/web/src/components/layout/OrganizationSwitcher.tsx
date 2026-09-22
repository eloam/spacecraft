import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Building2, Check, ChevronsUpDown, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn } from "@/lib/utils";
import { CreateOrganizationDialog } from "@/components/CreateOrganizationDialog";
import { useOrganizations } from "@/api/queries";
import { roleLabel } from "@/api/types";

/**
 * Above this many, the list stops being scannable and a filter earns its place. Below it,
 * a search box is a control to skip past on the way to a name already on screen.
 */
const SEARCH_THRESHOLD = 7;

/**
 * The current organization, beside the product name, with a way to change it.
 *
 * Putting it in the top bar means the answer to "which organization am I in?" is in the same
 * place on every page, and switching does not require going back to a list first.
 *
 * Switching lands on the chosen organization's root rather than the equivalent section: the
 * section you are in may not apply — a project page has no counterpart elsewhere — and
 * silently redirecting somewhere unrelated is worse than starting at the top.
 */
export function OrganizationSwitcher() {
  const { orgSlug = "" } = useParams();
  const organizations = useOrganizations();
  const [query, setQuery] = useState("");
  const [createOpen, setCreateOpen] = useState(false);

  const all = organizations.data ?? [];

  // Slug or id: a link minted from a stored record carries the id on purpose, so that
  // renaming an organization cannot break it. The shell rewrites that address to the slug,
  // but only after rendering it once, and the button names the organization either way.
  const current = all.find((o) => o.slug === orgSlug || o.id === orgSlug);

  const needle = query.trim().toLowerCase();
  const matches = needle
    ? // Slug as well as name: the slug is what people see in URLs, so it is what they
      // half-remember when looking for an organization.
      all.filter(
        (o) => o.name.toLowerCase().includes(needle) || o.slug.toLowerCase().includes(needle),
      )
    : all;

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            // Filled rather than ghost: this is the one control in the bar that says where
            // you are, and a background separates it from the wordmark it sits beside
            // instead of letting the two read as one long title.
            <Button variant="secondary" size="sm" className="min-w-0 gap-1.5 px-2.5 font-semibold">
              <Building2 className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              <span className="truncate">{current?.name ?? orgSlug}</span>
              <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
            </Button>
          }
          // The visible name stays in the accessible name (WCAG 2.5.3): a label that only said
          // what the button does left "click Ledger" with nothing to match.
          aria-label={`Switch organization, ${current?.name ?? orgSlug}`}
        />
        <DropdownMenuContent align="start" className="w-64">
          {all.length > SEARCH_THRESHOLD && (
            <div className="p-1">
              <Input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Find an organization"
                aria-label="Find an organization"
                className="h-8"
                // The menu treats typing as type-ahead navigation and would steal these keys;
                // the field needs them to be a field.
                onKeyDown={(e) => e.stopPropagation()}
              />
            </div>
          )}

          {/* Base UI requires a label to sit inside a group: its GroupLabel reads context
              the group provides, and rendering it loose throws at open time. */}
          <DropdownMenuGroup>
            <DropdownMenuLabel>Organizations</DropdownMenuLabel>

            {matches.length === 0 && (
              <p className="px-2 py-3 text-center text-sm text-muted-foreground">
                No organization matches “{query.trim()}”.
              </p>
            )}

            {matches.map((organization) => {
              const isCurrent = organization.id === current?.id;
              return (
                // A Link, not an onClick: Base UI closes the menu on its own press handling
                // and the click never reaches the callback — the same reason Settings in the
                // account menu is a link. It also makes these open in a new tab.
                <DropdownMenuItem
                  key={organization.id}
                  render={
                    <Link
                      to={`/orgs/${organization.slug}`}
                      aria-current={isCurrent ? "page" : undefined}
                    />
                  }
                >
                  <Check
                    className={cn("size-4", isCurrent ? "opacity-100" : "opacity-0")}
                    aria-hidden
                  />
                  <span className="flex-1 truncate">{organization.name}</span>
                  <span className="text-xs text-muted-foreground">
                    {roleLabel[organization.role] ?? "Unknown"}
                  </span>
                </DropdownMenuItem>
              );
            })}
          </DropdownMenuGroup>

          <DropdownMenuSeparator />

          {/* Not "all organizations": every one the user belongs to is already listed above,
              so that link led back to a longer version of this menu. Creating one is the
              action this place is actually missing. */}
          <DropdownMenuItem onClick={() => setCreateOpen(true)}>
            <Plus className="size-4" aria-hidden />
            New organization
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <CreateOrganizationDialog open={createOpen} onOpenChange={setCreateOpen} />
    </>
  );
}
