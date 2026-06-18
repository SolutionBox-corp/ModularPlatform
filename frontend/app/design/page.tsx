import type { Metadata } from "next";
import { DesignGallery } from "./design-gallery";

export const metadata: Metadata = {
  title: "Design Gallery — ModularPlatform",
};

/**
 * Dev-only component gallery. No auth guard intentional — this is a local/dev
 * tooling page only. Production deployments should gate it behind a route group
 * or middleware if needed.
 */
export default function DesignPage() {
  return <DesignGallery />;
}
