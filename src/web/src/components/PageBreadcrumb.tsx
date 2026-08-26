import { Fragment } from "react";
import { Link } from "react-router-dom";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
  breadcrumbLinkClass,
} from "@/components/ui/breadcrumb";

export type Crumb = { label: string; to?: string };

/**
 * The trail above a page heading, replacing the per-page back links that only ever led one
 * step up. A trail says where you are as well as how to leave, which a single arrow cannot.
 *
 * The last crumb is the current page and is never a link; anything before it must carry a
 * `to`. A crumb whose label is still loading should pass a placeholder rather than an empty
 * string, so the trail does not change width once the data arrives.
 */
export function PageBreadcrumb({ crumbs }: { crumbs: Crumb[] }) {
  if (crumbs.length === 0) {
    return null;
  }

  return (
    <Breadcrumb className="mb-3">
      <BreadcrumbList>
        {crumbs.map((crumb, index) => {
          const isLast = index === crumbs.length - 1;

          return (
            <Fragment key={`${crumb.label}-${index}`}>
              <BreadcrumbItem>
                {isLast || !crumb.to ? (
                  <BreadcrumbPage>{crumb.label}</BreadcrumbPage>
                ) : (
                  <Link to={crumb.to} className={breadcrumbLinkClass}>
                    {crumb.label}
                  </Link>
                )}
              </BreadcrumbItem>
              {!isLast && <BreadcrumbSeparator />}
            </Fragment>
          );
        })}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
