import { useEffect } from "react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { useOrganizations, useProjects } from "@/api/queries";

/** The form the API is addressed by, and the one no address bar should be showing. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Two names for the same thing, and one rule about which goes where.
 *
 * The API is addressed by id: every route it declares is `{organizationId:guid}`, and an id
 * never moves. The client is addressed by slug: `/orgs/acme-corp/members` is what somebody
 * reads, copies and pastes to a colleague, and a guid in that position names nothing.
 *
 * So the translation happens here, once, on the way in. A page takes the slug from its route,
 * asks for the id, and passes that to every query. Nothing else in the application has to
 * know that two identifiers exist.
 *
 * The lookup costs nothing in practice: the organization list is already loaded — the
 * switcher in the top bar needs it — and the project list is already loaded wherever projects
 * are shown. Both are cached for the session.
 */
export function useOrganizationId(slug: string): string {
  const organizations = useOrganizations();

  // Already an id: a link from a notification or an email carries one on purpose, because a
  // record written months ago must not depend on a name that can be changed.
  if (GUID.test(slug)) {
    return slug;
  }

  return organizations.data?.find((o) => o.slug === slug)?.id ?? "";
}

/** The project's id, from the list of the organization it belongs to. */
export function useProjectId(organizationId: string, slug: string): string {
  const projects = useProjects(organizationId);

  if (GUID.test(slug)) {
    return slug;
  }

  return projects.data?.find((p) => p.slug === slug)?.id ?? "";
}

/**
 * Puts the slug back in the address bar when a route was reached by id.
 *
 * Both forms work — the pages resolve either — but an id is a database detail and a URL is
 * something people read. A link from an email arrives spelled the durable way and is rewritten
 * to the readable one as soon as the name is known.
 *
 * The rewrite replaces the history entry rather than adding one: going back should return to
 * where the visitor came from, not to the same page spelled differently.
 */
export function useCanonicalRoute() {
  const { orgId = "", projectId = "" } = useParams();
  const location = useLocation();
  const navigate = useNavigate();

  const organizations = useOrganizations();
  const organizationId = useOrganizationId(orgId);
  const projects = useProjects(organizationId);

  const organizationSlug = GUID.test(orgId)
    ? organizations.data?.find((o) => o.id === orgId)?.slug
    : undefined;

  const projectSlug = GUID.test(projectId)
    ? projects.data?.find((p) => p.id === projectId)?.slug
    : undefined;

  useEffect(() => {
    if (!organizationSlug && !projectSlug) return;

    let path = location.pathname;

    if (organizationSlug) {
      path = path.replace(`/orgs/${orgId}`, `/orgs/${organizationSlug}`);
    }

    if (projectSlug) {
      path = path.replace(`/projects/${projectId}`, `/projects/${projectSlug}`);
    }

    if (path !== location.pathname) {
      navigate(`${path}${location.search}${location.hash}`, { replace: true });
    }
  }, [organizationSlug, projectSlug, orgId, projectId, location, navigate]);
}
