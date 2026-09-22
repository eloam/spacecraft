import { useParams } from "react-router-dom";
import { FileCode2, Package, Settings } from "lucide-react";
import { SectionNav } from "@/components/layout/SectionNav";

/**
 * The project's sections.
 *
 * Rendered as routes rather than local tab state, so a section can be linked to, opened in
 * a new tab and reached with the browser's back button — and so settings can sit alongside
 * the others instead of being a lone link among tabs that behave differently.
 */
export function ProjectNav() {
  const { orgSlug = "", projectSlug = "" } = useParams();
  const base = `/orgs/${orgSlug}/projects/${projectSlug}`;

  return (
    // The rule sits on the wrapper rather than the nav, so -mb-px can lift the active tab's
    // underline over it instead of stacking the two. It is safe there: on the nav it cannot
    // cancel the margin-block-end the layout puts after the wrapper, which is what it did
    // when this element carried the border itself.
    <div className="border-b">
      <SectionNav
        label="Project sections"
        tabs={[
          {
            label: "Specs",
            to: base,
            icon: <FileCode2 className="size-4" aria-hidden />,
            end: true,
          },
          {
            label: "SDK targets",
            to: `${base}/targets`,
            icon: <Package className="size-4" aria-hidden />,
          },
          {
            label: "Settings",
            to: `${base}/settings`,
            icon: <Settings className="size-4" aria-hidden />,
          },
        ]}
      />
    </div>
  );
}
