import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DesignGallery } from "./design-gallery";

export const metadata: Metadata = {
  title: "Design Gallery — ModularPlatform",
};

/**
 * Dev-only component gallery. Renders a 404 in production so the design
 * sandbox is never exposed in shipped builds.
 */
export default function DesignPage() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }
  return <DesignGallery />;
}
