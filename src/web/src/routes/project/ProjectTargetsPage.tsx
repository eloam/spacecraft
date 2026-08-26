import { useState } from "react";
import { useParams } from "react-router-dom";
import { Package, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/EmptyState";
import { QueryBoundary } from "@/components/QueryBoundary";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";
import { AddTargetDialog } from "@/routes/project/AddTargetDialog";
import { ApiError } from "@/api/client";
import { useDeleteTarget, useProject } from "@/api/queries";
import { languageLabel, registryLabel } from "@/api/types";

/** One SDK per language and registry, and what each has published. */
export function ProjectTargetsPage() {
  const { orgId = "", projectId = "" } = useParams();
  const project = useProject(orgId, projectId);
  const deleteTarget = useDeleteTarget(orgId, projectId);

  const [targetOpen, setTargetOpen] = useState(false);
  const [targetToDelete, setTargetToDelete] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button variant="outline" onClick={() => setTargetOpen(true)}>
          <Plus className="size-4" aria-hidden />
          Add target
        </Button>
      </div>

      <QueryBoundary isLoading={project.isLoading} error={project.error}>
        {project.data?.targets.length ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Language</TableHead>
                <TableHead>Package</TableHead>
                <TableHead>Registry</TableHead>
                <TableHead>Auto-publish</TableHead>
                <TableHead className="text-right">Last published</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {project.data.targets.map((target) => (
                <TableRow key={target.id}>
                  <TableCell>{languageLabel[target.language]}</TableCell>
                  <TableCell className="font-mono text-xs">{target.packageName}</TableCell>
                  <TableCell>{registryLabel[target.registry]}</TableCell>
                  <TableCell>
                    <Badge variant={target.autoPublish ? "default" : "secondary"}>
                      {target.autoPublish ? "On" : "Off"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right font-mono text-xs">
                    {target.lastPublishedVersion ?? "—"}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setTargetToDelete(target.id)}
                    >
                      <Trash2 className="size-4" aria-hidden />
                      <span className="sr-only">Delete {target.packageName}</span>
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <EmptyState
            icon={<Package className="size-8" />}
            title="No SDK targets"
            description="A target says which language to emit and which package name to publish it under."
            action={
              <Button variant="outline" onClick={() => setTargetOpen(true)}>
                <Plus className="size-4" aria-hidden />
                Add target
              </Button>
            }
          />
        )}
      </QueryBoundary>

      <AddTargetDialog
        orgId={orgId}
        projectId={projectId}
        open={targetOpen}
        onOpenChange={setTargetOpen}
      />

      <ConfirmDeleteDialog
        open={targetToDelete !== null}
        onOpenChange={(open) => {
          if (!open) {
            setTargetToDelete(null);
            setDeleteError(null);
          }
        }}
        title="Delete this SDK target?"
        description="The target and the runs that produced it are removed."
        confirmText={project.data?.targets.find((t) => t.id === targetToDelete)?.packageName}
        consequences={[
          "Runs for this target, with their artifacts and logs",
          "Packages already published stay in their registries",
        ]}
        isPending={deleteTarget.isPending}
        error={deleteError}
        onConfirm={() =>
          targetToDelete &&
          deleteTarget.mutate(
            {
              targetId: targetToDelete,
              packageName:
                project.data?.targets.find((t) => t.id === targetToDelete)?.packageName ?? "",
            },
            {
              onSuccess: () => {
                toast.success("SDK target deleted.");
                setTargetToDelete(null);
              },
              onError: (error) =>
                setDeleteError(
                  error instanceof ApiError ? error.detail : "Could not delete the target.",
                ),
            },
          )
        }
      />
    </div>
  );
}
