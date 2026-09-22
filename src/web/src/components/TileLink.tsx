import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Item } from "@/components/ui/item";

/**
 * A tile that is a link in its entirety, for the organization and project grids.
 *
 * It used to be a card with an absolutely positioned link stretched over it and a settings
 * cog floating above that, stacking three things where one was meant: Item renders as the
 * anchor itself, so the hover state and the focus ring come with it.
 *
 * Hover has to lift, and the two themes have different room to do it in. Dark lightens the
 * fill, card to muted, moving away from the ground. Light cannot: the card is already white,
 * and every tint available is a step *towards* the page grey — which is why Item's own
 * bg-muted hover read as the tile sinking. So light keeps the fill and lifts with a shadow
 * instead, the way a white card on grey is meant to. Both get the darker border.
 */
export function TileLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Item
      variant="outline"
      className="h-full flex-col items-stretch bg-card transition-[background-color,border-color,box-shadow] hover:border-foreground/20 hover:shadow-md dark:hover:shadow-none [a]:hover:bg-card dark:[a]:hover:bg-muted/50"
      render={<Link to={to} />}
    >
      {children}
    </Item>
  );
}
