"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  MoreVerticalIcon,
  DownloadIcon,
  PencilIcon,
  Trash2Icon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useDeleteFile, useRenameFile } from "@/features/files/hooks";
import { getFileDownloadHref, type FileListItem } from "@/features/files/api";

/**
 * Per-row file actions: a kebab menu with Download / Rename / Delete. Rename and
 * Delete open controlled dialogs (rendered outside the menu to avoid focus races).
 */
export function FileRowActions({ file }: { file: FileListItem }) {
  const t = useTranslations("files");
  const [renameOpen, setRenameOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [newName, setNewName] = useState(file.fileName);

  const rename = useRenameFile();
  const remove = useDeleteFile();

  function openRename() {
    setNewName(file.fileName);
    setRenameOpen(true);
  }

  function submitRename() {
    const name = newName.trim();
    if (name === "" || name === file.fileName) {
      setRenameOpen(false);
      return;
    }
    rename.mutate(
      { id: file.id, fileName: name },
      {
        onSuccess: () => {
          toast.success(t("actions.renamed"));
          setRenameOpen(false);
        },
        onError: () => toast.error(t("actions.renameError")),
      },
    );
  }

  function submitDelete() {
    remove.mutate(file.id, {
      onSuccess: () => {
        toast.success(t("actions.deleted", { name: file.fileName }));
        setDeleteOpen(false);
      },
      onError: () => toast.error(t("actions.deleteError")),
    });
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={t("actions.menuAria", { name: file.fileName })}
            />
          }
        >
          <MoreVerticalIcon className="h-4 w-4" aria-hidden="true" />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem
            render={
              <a href={getFileDownloadHref(file.id)} download={file.fileName} />
            }
          >
            <DownloadIcon className="h-4 w-4" aria-hidden="true" />
            {t("actions.download")}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={openRename}>
            <PencilIcon className="h-4 w-4" aria-hidden="true" />
            {t("actions.rename")}
          </DropdownMenuItem>
          <DropdownMenuItem
            variant="destructive"
            onClick={() => setDeleteOpen(true)}
          >
            <Trash2Icon className="h-4 w-4" aria-hidden="true" />
            {t("actions.delete")}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      {/* Rename dialog */}
      <Dialog open={renameOpen} onOpenChange={setRenameOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("actions.renameTitle")}</DialogTitle>
            <DialogDescription>{t("actions.renameDescription")}</DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5 py-2">
            <Label htmlFor="rename-input">{t("actions.renameLabel")}</Label>
            <Input
              id="rename-input"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              maxLength={512}
              autoFocus
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  submitRename();
                }
              }}
            />
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setRenameOpen(false)}
              disabled={rename.isPending}
            >
              {t("actions.cancel")}
            </Button>
            <Button
              onClick={submitRename}
              disabled={rename.isPending || newName.trim() === ""}
            >
              {rename.isPending ? t("actions.saving") : t("actions.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete confirm dialog */}
      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("actions.deleteTitle")}</DialogTitle>
            <DialogDescription>
              {t("actions.deleteDescription", { name: file.fileName })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setDeleteOpen(false)}
              disabled={remove.isPending}
            >
              {t("actions.cancel")}
            </Button>
            <Button
              variant="destructive"
              onClick={submitDelete}
              disabled={remove.isPending}
            >
              {remove.isPending ? t("actions.deleting") : t("actions.delete")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
