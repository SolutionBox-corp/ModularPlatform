"use client";

import { TenantEntitlementEditor } from "./tenant-entitlement-editor";

/**
 * Main content for /platform/tenants.
 *
 * NOTE: The backend has no list-tenants endpoint, so we cannot render a
 * DataTable of all tenants. The workflow is:
 *   1. Admin uses "Provision tenant" (page header) → gets back a tenantId.
 *   2. Admin enters that ID here to manage entitlements and invite tokens.
 *
 * When GET /tenant/admin/tenants is added, this component should fetch the list
 * and render it in a DataTable with a "Manage" link per row leading to
 * /platform/tenants/{tenantId}.
 */
export function TenantsContent() {
  return (
    <div className="space-y-6">
      <TenantEntitlementEditor />
    </div>
  );
}
