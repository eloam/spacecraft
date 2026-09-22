import type { ReactNode } from "react";
import { Card, CardContent } from "@/components/ui/card";

/**
 * The frame every table sits in: one card, flush to its edges, with the table's own header
 * band. A footer — a `CardFooter` — goes beside the content rather than inside it, where the
 * card's own spacing applies.
 */
export function TableCard({ children, footer }: { children: ReactNode; footer?: ReactNode }) {
  return (
    <Card className="py-0">
      <CardContent className="p-0">{children}</CardContent>
      {footer}
    </Card>
  );
}
