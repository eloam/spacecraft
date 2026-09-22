import { Bell, KeyRound, ShieldAlert, UserRound } from "lucide-react";
import { SectionNav } from "@/components/layout/SectionNav";

/**
 * The account's sections, in the same shape organizations and projects use.
 *
 * Splitting one long page into routed sections means a link can point at the part being
 * discussed — "change your password" can land on the password form rather than the top of a
 * page the reader then has to scan.
 */
export function AccountNav() {
  return (
    // The rule sits on the wrapper rather than the nav, so -mb-px can lift the active tab's
    // underline over it instead of stacking the two. It is safe there: on the nav it cannot
    // cancel the margin-block-end the layout puts after the wrapper, which is what it did
    // when this element carried the border itself.
    <div className="border-b">
      <SectionNav
        label="Account sections"
        tabs={[
          {
            label: "Profile",
            to: "/account",
            icon: <UserRound className="size-4" aria-hidden />,
            end: true,
          },
          {
            label: "Security",
            to: "/account/security",
            icon: <KeyRound className="size-4" aria-hidden />,
          },
          {
            label: "Notifications",
            to: "/account/notifications",
            icon: <Bell className="size-4" aria-hidden />,
          },
          {
            label: "Danger zone",
            to: "/account/danger",
            icon: <ShieldAlert className="size-4" aria-hidden />,
          },
        ]}
      />
    </div>
  );
}
