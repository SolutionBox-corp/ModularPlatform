"use client";

import { useState } from "react";
import { PencilIcon, PlusIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import {
  useAdminPackages,
  useCreatePackage,
  useUpdatePackage,
} from "@/features/platform/hooks";
import type { AdminCreditPackage } from "@/features/platform/api";

interface FormState {
  name: string;
  creditAmount: string;
  price: string;
  currency: string;
  bucketExpiryDays: string;
  active: boolean;
  stripePriceId: string;
}

function toForm(pkg: AdminCreditPackage | null): FormState {
  return {
    name: pkg?.name ?? "",
    creditAmount: pkg ? String(pkg.creditAmount) : "",
    price: pkg ? String(pkg.price) : "",
    currency: pkg?.currency ?? "EUR",
    bucketExpiryDays: pkg?.bucketExpiryDays != null ? String(pkg.bucketExpiryDays) : "",
    active: pkg?.active ?? true,
    stripePriceId: pkg?.stripePriceId ?? "",
  };
}

/**
 * Platform-admin credit-package (product) management. Lists the catalogue (active + inactive) and creates/edits
 * packages via the billing.manage-gated admin endpoints. The platform admin (no tenant) manages the GLOBAL catalogue.
 */
export function ProductsContent() {
  const t = useTranslations("platform");
  const locale = useLocale();
  const { data, isLoading } = useAdminPackages();
  const createMutation = useCreatePackage();
  const updateMutation = useUpdatePackage();

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<AdminCreditPackage | null>(null);
  const [form, setForm] = useState<FormState>(toForm(null));

  function openCreate() {
    setEditing(null);
    setForm(toForm(null));
    setDialogOpen(true);
  }

  function openEdit(pkg: AdminCreditPackage) {
    setEditing(pkg);
    setForm(toForm(pkg));
    setDialogOpen(true);
  }

  async function handleSave() {
    const bucketExpiryDays = form.bucketExpiryDays.trim()
      ? Number(form.bucketExpiryDays)
      : null;
    const base = {
      name: form.name.trim(),
      creditAmount: Number(form.creditAmount),
      price: Number(form.price),
      bucketExpiryDays,
      active: form.active,
      stripePriceId: form.stripePriceId.trim() || null,
    };
    try {
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, input: base });
      } else {
        await createMutation.mutateAsync({
          ...base,
          currency: form.currency.trim().toUpperCase(),
        });
      }
      setDialogOpen(false);
    } catch {
      // Error toast surfaces via the global mutation cache.
    }
  }

  const saving = createMutation.isPending || updateMutation.isPending;
  const formValid =
    form.name.trim().length > 0 &&
    Number(form.creditAmount) > 0 &&
    Number(form.price) >= 0 &&
    (editing ? true : form.currency.trim().length === 3);

  const columns: ColumnDef<AdminCreditPackage>[] = [
    {
      key: "name",
      header: t("products.table.name"),
      cell: (row) => <span className="text-sm font-medium">{row.name}</span>,
    },
    {
      key: "credits",
      header: t("products.table.credits"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">{row.creditAmount}</span>
      ),
    },
    {
      key: "price",
      header: t("products.table.price"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {row.price.toLocaleString(locale, { style: "currency", currency: row.currency })}
        </span>
      ),
    },
    {
      key: "expiry",
      header: t("products.table.expiry"),
      className: "hidden md:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {row.bucketExpiryDays != null ? `${row.bucketExpiryDays} d` : "—"}
        </span>
      ),
    },
    {
      key: "active",
      header: t("products.table.status"),
      cell: (row) => (
        <Badge variant={row.active ? "secondary" : "outline"} className="text-xs">
          {row.active ? t("products.active") : t("products.inactive")}
        </Badge>
      ),
    },
    {
      key: "actions",
      header: "",
      className: "text-right w-20",
      cell: (row) => (
        <Button size="sm" variant="ghost" onClick={() => openEdit(row)}>
          <PencilIcon className="h-3.5 w-3.5 mr-1.5" />
          {t("products.edit")}
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("products.heading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">{t("products.description")}</p>
        </div>
        <Button size="sm" onClick={openCreate}>
          <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
          {t("products.newPackage")}
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={data}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        emptyTitle={t("products.table.emptyTitle")}
        emptyDescription={t("products.table.emptyDescription")}
      />

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {editing ? t("products.dialog.editTitle") : t("products.dialog.createTitle")}
            </DialogTitle>
            <DialogDescription>{t("products.dialog.description")}</DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label htmlFor="pkg-name">{t("products.dialog.name")}</Label>
              <Input
                id="pkg-name"
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                autoComplete="off"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="pkg-credits">{t("products.dialog.credits")}</Label>
                <Input
                  id="pkg-credits"
                  type="number"
                  min={1}
                  value={form.creditAmount}
                  onChange={(e) => setForm((f) => ({ ...f, creditAmount: e.target.value }))}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="pkg-price">{t("products.dialog.price")}</Label>
                <Input
                  id="pkg-price"
                  type="number"
                  min={0}
                  step="0.01"
                  value={form.price}
                  onChange={(e) => setForm((f) => ({ ...f, price: e.target.value }))}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="pkg-currency">{t("products.dialog.currency")}</Label>
                <Input
                  id="pkg-currency"
                  value={form.currency}
                  maxLength={3}
                  disabled={!!editing}
                  onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value.toUpperCase() }))}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="pkg-expiry">{t("products.dialog.expiry")}</Label>
                <Input
                  id="pkg-expiry"
                  type="number"
                  min={1}
                  placeholder={t("products.dialog.expiryHint")}
                  value={form.bucketExpiryDays}
                  onChange={(e) => setForm((f) => ({ ...f, bucketExpiryDays: e.target.value }))}
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="pkg-stripe">{t("products.dialog.stripePriceId")}</Label>
              <Input
                id="pkg-stripe"
                value={form.stripePriceId}
                placeholder="price_…"
                onChange={(e) => setForm((f) => ({ ...f, stripePriceId: e.target.value }))}
                autoComplete="off"
                className="font-mono text-xs"
              />
            </div>

            <div className="flex items-center justify-between rounded-lg border px-3 py-2.5">
              <Label htmlFor="pkg-active" className="text-sm font-medium">
                {t("products.dialog.active")}
              </Label>
              <Switch
                id="pkg-active"
                checked={form.active}
                onCheckedChange={(checked) => setForm((f) => ({ ...f, active: checked }))}
              />
            </div>
          </div>

          <DialogFooter>
            <Button onClick={handleSave} disabled={!formValid || saving}>
              {saving ? t("products.dialog.saving") : t("products.dialog.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
