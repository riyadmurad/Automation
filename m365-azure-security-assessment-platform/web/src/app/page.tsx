import { isSyntheticPreviewEnabled } from "@/lib/preview-policy";

export const dynamic = "force-dynamic";

export default async function Home() {
  if (process.env.NODE_ENV !== "production" && isSyntheticPreviewEnabled(process.env)) {
    const [{ default: PreviewConsole }, { loadSyntheticData }] = await Promise.all([
      import("@/components/preview-console"),
      import("@/lib/synthetic-data"),
    ]);
    return <PreviewConsole data={loadSyntheticData()} />;
  }

  return (
    <main className="secure-view">
      <div className="secure-mark" aria-hidden="true">SA</div>
      <p className="eyebrow">M365 + Azure / Security assessment</p>
      <h1>Workspace unavailable</h1>
      <p>Operator authentication and customer authorization are not configured.</p>
      <div className="secure-state"><span className="status-dot" /> Access blocked</div>
      <p className="muted">No customer data loaded. Connections and execution remain disabled.</p>
    </main>
  );
}
