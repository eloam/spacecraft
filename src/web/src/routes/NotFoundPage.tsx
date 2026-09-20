import { SearchX } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/EmptyState";

/**
 * What an address nobody declared leads to.
 *
 * The catch-all used to send every unknown URL to the dashboard, which turned a typo or a
 * link to a page that has since moved into a silent arrival somewhere else. Saying so leaves
 * the address in the bar for the visitor to correct, and gives them one link out.
 */
export function NotFoundPage() {
  return (
    <>
      <title>Page not found — SpaceCraft</title>

      <EmptyState
        icon={<SearchX />}
        title="Page not found"
        description="There is nothing at this address. Check the link, or start again from your organizations."
        action={
          <Button variant="outline" render={<Link to="/" />}>
            Go to your organizations
          </Button>
        }
      />
    </>
  );
}
