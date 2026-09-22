import { useSearchParams } from "react-router-dom";
import { returnPathIn, returnQueryIn } from "@/lib/returnTo";

/** Where the current page should continue to once there is a session. */
export function useReturnPath(): string {
  const [params] = useSearchParams();
  return returnPathIn(params);
}

/** The query string that carries the same destination to the other authentication page. */
export function useReturnQuery(): string {
  const [params] = useSearchParams();
  return returnQueryIn(params);
}
