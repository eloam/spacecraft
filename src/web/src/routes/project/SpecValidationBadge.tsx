import { Badge } from "@/components/ui/badge";
import { SpecValidationStatus } from "@/api/types";

export function SpecValidationBadge({
  status,
  information,
  warnings,
  errors,
}: {
  status: number;
  information: number;
  warnings: number;
  errors: number;
}) {
  if (status === SpecValidationStatus.Invalid) {
    return <Badge variant="destructive">{errors} error{errors === 1 ? "" : "s"}</Badge>;
  }
  if (status === SpecValidationStatus.ValidWithWarnings) {
    return <Badge variant="secondary">{warnings} warning{warnings === 1 ? "" : "s"}</Badge>;
  }
  if (status === SpecValidationStatus.Valid) {
    // Valid, but not silent. Without this the badge would read "Valid" on a document with
    // hundreds of notes and give no hint that there is anything to open.
    return information > 0 ? (
      <Badge variant="outline">{information} note{information === 1 ? "" : "s"}</Badge>
    ) : (
      <Badge variant="outline">Valid</Badge>
    );
  }
  return <Badge variant="secondary">Pending</Badge>;
}
