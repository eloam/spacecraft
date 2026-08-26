import { Badge } from "@/components/ui/badge";
import { SpecValidationStatus } from "@/api/types";

export function SpecValidationBadge({
  status,
  warnings,
  errors,
}: {
  status: number;
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
    return <Badge variant="outline">Valid</Badge>;
  }
  return <Badge variant="secondary">Pending</Badge>;
}
