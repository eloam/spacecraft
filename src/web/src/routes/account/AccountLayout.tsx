import { Outlet } from "react-router-dom";
import { Skeleton } from "@/components/ui/skeleton";
import { AccountAvatar } from "@/components/AccountAvatar";
import { AccountNav } from "@/components/layout/AccountNav";
import { useAccount } from "@/api/queries";

/** Frame for the account sections: who you are, then what you can change. */
export function AccountLayout() {
  const account = useAccount();

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <AccountAvatar
          displayName={account.data?.displayName}
          email={account.data?.email}
          className="size-9 text-sm"
        />
        <div className="min-w-0">
          {account.isPending ? (
            <Skeleton className="h-5 w-40" />
          ) : (
            <>
              {/* A name even when the account failed to load: the section below says what
                  went wrong, and a frame with an empty title said nothing at all. */}
              <h1 className="truncate font-heading text-lg font-medium tracking-tight">
                {account.data?.displayName || account.data?.email || "Your account"}
              </h1>
              {account.data?.displayName && (
                <p className="truncate text-sm text-muted-foreground">{account.data.email}</p>
              )}
            </>
          )}
        </div>
      </div>

      <AccountNav />

      <Outlet />
    </div>
  );
}
