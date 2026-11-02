"use client";

import { useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { Activity, ArrowDownToLine, ArrowRight, ArrowUpDown, Check, ChevronRight, CircleHelp, ClipboardList, FileSearch, Fingerprint, LayoutDashboard, ListFilter, LockKeyhole, Search, ShieldCheck, SlidersHorizontal, TriangleAlert, X } from "lucide-react";
import { auditValueText, createExport, filterAudit, filterFindings, redact, STATUSES, type AuditEvent, type ControlStatus, type Filters, type Finding, type PreviewData } from "@/lib/console-model";

const views = [
    { name: "Overview", icon: LayoutDashboard },
    { name: "Assessments", icon: ClipboardList },
    { name: "Findings", icon: FileSearch },
    { name: "Coverage", icon: SlidersHorizontal },
    { name: "Audit", icon: Fingerprint },
] as const;
type View = (typeof views)[number]["name"];
type Detail = { kind: "finding"; row: Finding } | { kind: "audit"; row: AuditEvent };
const storageKey = "security-assessment.synthetic-tenant.v1";
const storageEvent = "synthetic-tenant-change";
let memoryTenant = "";

function subscribeTenant(callback: () => void) {
    window.addEventListener("storage", callback);
    window.addEventListener(storageEvent, callback);
    return () => {
        window.removeEventListener("storage", callback);
        window.removeEventListener(storageEvent, callback);
    };
}

function storedTenant() {
    try { return window.localStorage.getItem(storageKey) ?? memoryTenant; }
    catch { return memoryTenant; }
}

function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: string }) {
    return <span className={`badge ${tone}`}><span className="badge-dot" />{children}</span>;
}

function statusTone(status: ControlStatus) {
    if (status === "Compliant") return "green";
    if (status === "Noncompliant") return "red";
    if (status === "Unable to assess") return "amber";
    return "neutral";
}

function Empty({ clear }: { clear: () => void }) {
    return <div className="empty-state"><FileSearch size={28} aria-hidden="true" /><h3>No matching records</h3><button className="text-button" onClick={clear}><X size={14} />Clear filters</button></div>;
}

function Value({ value }: { value: unknown }) {
    return <pre>{JSON.stringify(redact(value), null, 2)}</pre>;
}

function DetailDrawer({ detail, close, customerName }: { detail: Detail; close: () => void; customerName: string }) {
    const dialog = useRef<HTMLDialogElement>(null);
    const opener = useRef<HTMLElement | null>(null);
    useEffect(() => {
        const element = dialog.current;
        if (element && !element.open) {
            opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
            element.showModal();
        }
    }, []);
    const row = detail.row;
    return (
        <dialog ref={dialog} className="drawer" onClose={() => { opener.current?.focus(); close(); }} aria-labelledby="detail-title">
            <div className="drawer-inner">
                <header className="drawer-header"><span className="eyebrow">{detail.kind === "audit" ? "Audit event" : "Finding evidence"}</span><button className="icon-button" title="Close detail" aria-label="Close detail" onClick={() => dialog.current?.close()}><X size={20} /></button></header>
                <Badge tone="amber">Synthetic preview</Badge>
                <h2 id="detail-title">{detail.kind === "audit" ? detail.row.setting : detail.row.title}</h2>
                <p className="muted">{customerName}</p><code className="tenant-id">{row.tenantId}</code>
                <dl className="detail-grid"><dt>Service</dt><dd>{row.service}</dd><dt>Resource</dt><dd>{row.resource}</dd><dt>Provenance</dt><dd>{row.provenance}</dd></dl>
                {detail.kind === "finding" ? <>
                    <div className="detail-status"><Badge tone={statusTone(detail.row.status)}>{detail.row.status}</Badge><span>{detail.row.controlId}</span></div>
                    <p>{detail.row.reason}</p>
                    <h3>Observed fixture value</h3><Value value={detail.row.observed} />
                    <h3>Expected fixture value</h3><Value value={detail.row.expected} />
                    <dl className="detail-grid"><dt>Evidence condition</dt><dd>{detail.row.evidenceState}</dd><dt>Collected UTC</dt><dd>{detail.row.collectedAtUtc}</dd><dt>Assessment</dt><dd>{detail.row.assessmentId}</dd><dt>Fixture pack</dt><dd>{detail.row.baselineVersion}</dd><dt>Source revision</dt><dd>Unverified / activation blocked</dd><dt>Evidence</dt><dd>{detail.row.evidenceReference}</dd><dt>Permissions / license</dt><dd>Unverified; manual review required</dd></dl>
                    <div className="notice"><LockKeyhole size={17} /><div><strong>Guidance only</strong><p>No qualified operation, impact, verification or recovery evidence. Remediation and approval are unavailable. Emergency-access protections remain unchanged.</p></div></div>
                </> : <>
                    <div className="detail-status"><Badge tone={detail.row.outcome === "Verified" ? "green" : "amber"}>{detail.row.outcome}</Badge><code>{detail.row.id}</code></div>
                    <h3>Previous value</h3><Value value={detail.row.previous} />
                    <h3>Requested value</h3><Value value={detail.row.requested} />
                    <h3>Verified observed value</h3><Value value={detail.row.verified} />
                    <dl className="detail-grid"><dt>Timestamp UTC</dt><dd>{detail.row.timestampUtc}</dd><dt>Organization</dt><dd>{detail.row.organizationId}</dd><dt>Authorized user</dt><dd>{detail.row.authorizedUser}<br />{detail.row.authorizedUserId}</dd><dt>Requester</dt><dd>{detail.row.requester}</dd><dt>Approver</dt><dd>{detail.row.approver}</dd><dt>Executor</dt><dd>{detail.row.executor}</dd><dt>Plan revision</dt><dd>{detail.row.planRevision}</dd><dt>Operation</dt><dd>{detail.row.operationId}</dd><dt>Correlation</dt><dd>{detail.row.correlationId}</dd></dl>
                    <div className="notice"><CircleHelp size={17} /><p>Illustrative append-only event chain. Requested values do not prove an observed result. No customer setting was changed.</p></div>
                </>}
            </div>
        </dialog>
    );
}

export default function PreviewConsole({ data }: { data: PreviewData }) {
    const selectedTenant = useSyncExternalStore(subscribeTenant, storedTenant, () => "");
    const customer = data.customers.find((item) => item.tenantId === selectedTenant) ?? data.customers[0];
    return <CustomerWorkspace key={customer.tenantId} data={data} customer={customer} />;
}

function CustomerWorkspace({ data, customer }: { data: PreviewData; customer: PreviewData["customers"][number] }) {
    const [view, setView] = useState<View>("Overview");
    const [filters, setFilters] = useState<Filters>({});
    const [detail, setDetail] = useState<Detail | null>(null);
    const [error, setError] = useState("");
    const [announcement, setAnnouncement] = useState("");
    const [sortAscending, setSortAscending] = useState(true);
    const tenantFindings = filterFindings(data.findings, customer.tenantId);
    const findings = filterFindings(data.findings, customer.tenantId, filters).sort((first, second) => sortAscending ? first.controlId.localeCompare(second.controlId) : second.controlId.localeCompare(first.controlId));
    const audit = filterAudit(data.audit, customer.tenantId, filters);
    const services = data.services.filter((service) => (!filters.service || service.service === filters.service) && `${service.service} ${service.id}`.toLowerCase().includes((filters.search ?? "").toLowerCase()));
    const counts = Object.fromEntries(STATUSES.map((status) => [status, tenantFindings.filter((finding) => finding.status === status).length])) as Record<ControlStatus, number>;

    function navigate(next: View) {
        setView(next);
        setFilters({});
        setDetail(null);
        setError("");
        setAnnouncement("");
    }

    function updateFilter(key: keyof Filters, value: string) {
        setFilters((current) => ({ ...current, [key]: value }));
        setAnnouncement("");
    }

    function changeTenant(tenantId: string) {
        if (!data.customers.some((item) => item.tenantId === tenantId)) return;
        memoryTenant = tenantId;
        try { window.localStorage.setItem(storageKey, tenantId); }
        catch { setError("Customer selection could not be persisted. This session remains synthetic only."); }
        window.dispatchEvent(new Event(storageEvent));
    }

    function download(format: "csv" | "json") {
        setError("");
        try {
            const kind = view === "Audit" ? "audit" : "findings";
            const content = createExport(kind, format, customer.tenantId, data, filters);
            const blob = new Blob([content], { type: format === "csv" ? "text/csv;charset=utf-8" : "application/json;charset=utf-8" });
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = `SYNTHETIC-${kind}-${customer.tenantId}.${format}`;
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            URL.revokeObjectURL(url);
            setAnnouncement(`Synthetic ${format.toUpperCase()} export prepared for ${customer.name}.`);
        } catch { setError("Export could not be prepared. No data was sent. Try again."); }
    }

    return (
        <div className="console-shell">
            <a className="skip-link" href="#workspace">Skip to workspace</a>
            <aside className="sidebar">
                <div className="wordmark"><span className="brand-symbol"><ShieldCheck size={23} /></span><span>Security<br /><strong>Assessment</strong></span></div>
                <div className="sidebar-label">Operator workspace</div>
                <nav aria-label="Main navigation">{views.map(({ name, icon: Icon }) => <button key={name} onClick={() => navigate(name)} aria-current={view === name ? "page" : undefined}><Icon size={18} /><span>{name}</span>{name === "Findings" && <span className="nav-count">{tenantFindings.length}</span>}</button>)}</nav>
                <div className="sidebar-bottom"><div className="environment"><span className="status-dot" /> Local / synthetic</div><div className="sidebar-lock"><LockKeyhole size={15} /><span>Execution disabled</span></div><div className="operator"><span className="avatar">SR</span><div>Synthetic reviewer<small>Preview workspace</small></div></div></div>
            </aside>
            <div className="workspace-shell">
                <header className="topbar"><div className="breadcrumbs">Workspace <ChevronRight size={14} /><strong>{view}</strong></div><Badge tone="amber">Synthetic preview</Badge></header>
                <section className="customer-strip" aria-label="Current customer">
                    <div className="customer-selector"><span className="customer-initial" aria-hidden="true">{customer.name.endsWith("A") ? "A" : "B"}</span><label>Customer<select aria-label="Customer" value={customer.tenantId} onChange={(event) => changeTenant(event.target.value)}>{data.customers.map((item) => <option key={item.tenantId} value={item.tenantId}>{item.name}</option>)}</select></label></div>
                    <div className="tenant-context"><span>Tenant ID</span><code>{customer.tenantId}</code></div>
                    <Badge>Not connected</Badge>
                </section>
                <main id="workspace" className="workspace" tabIndex={-1}>
                    <div className="page-heading"><div><p className="eyebrow">M365 + Azure security</p><h1>{view === "Overview" ? "Customer overview" : view === "Coverage" ? "Service coverage" : view === "Audit" ? "Setting-level audit" : view}</h1><p className="muted">{view === "Audit" ? "Illustrative event history / 01 Jan 2026 / UTC" : "Synthetic scenario pack v1 / Source revisions unverified"}</p></div>{(view === "Findings" || view === "Audit" || view === "Assessments") && <div className="export-actions"><button className="button" onClick={() => download("csv")}><ArrowDownToLine size={16} />CSV</button><button className="button" onClick={() => download("json")}><ArrowDownToLine size={16} />JSON</button></div>}</div>
                    <div className="preview-notice"><TriangleAlert size={17} /><span><strong>Synthetic preview.</strong> Fixture examples only. No live evidence, active connections or customer writes. Scenario labels are illustrative, not evaluation results.</span></div>
                    {error && <div role="alert" className="error-banner"><TriangleAlert size={18} /><span>{error}</span><button aria-label="Dismiss error" className="icon-button" onClick={() => setError("")}><X size={16} /></button></div>}
                    <output className="sr-only">{announcement}</output>

                    {view === "Overview" && <>
                        <section className="summary-band" aria-label="Workspace summary">
                            <div><span className="metric-label">Service families</span><strong>{data.services.length}<small> / 13</small></strong><span>All manual / unverified</span></div>
                            <div><span className="metric-label">Live evidence coverage</span><strong>0<small> / 13</small></strong><span>No qualified connectors</span></div>
                            <div><span className="metric-label">Fixture findings</span><strong>{tenantFindings.length}</strong><span>6 illustrative status types</span></div>
                            <div><span className="metric-label">Qualified write operations</span><strong>0</strong><span className="metric-safe"><LockKeyhole size={13} />Feasibility gate pending</span></div>
                        </section>
                        <section className="status-section"><div className="section-heading"><h2>Fixture status distribution</h2><span className="muted">{tenantFindings.length} synthetic controls / not a compliance score</span></div><div className="distribution" aria-hidden="true">{STATUSES.map((status) => <span key={status} className={`segment ${statusTone(status)} segment-${STATUSES.indexOf(status)}`} style={{ flex: counts[status] }} />)}</div><div className="status-legend">{STATUSES.map((status) => <button key={status} onClick={() => { navigate("Findings"); setFilters({ status }); }}><span className={`legend-dot legend-${STATUSES.indexOf(status)}`} /><span>{status}</span><strong>{counts[status]}</strong></button>)}</div></section>
                        <section className="overview-columns"><div><div className="section-heading"><h2>Review queue</h2><button className="text-button" onClick={() => navigate("Findings")}>All findings<ArrowRight size={15} /></button></div><div className="review-list">{tenantFindings.filter((row) => row.status !== "Compliant" && row.status !== "Not applicable").slice(0, 4).map((row) => <button key={row.id} className="review-row" onClick={() => setDetail({ kind: "finding", row })}><span className={`severity-mark ${row.severity.toLowerCase()}`} /><div><strong>{row.title}</strong><span>{row.service} <span aria-hidden="true">/</span> {row.controlId}</span></div><Badge tone={statusTone(row.status)}>{row.status}</Badge><ChevronRight size={16} /></button>)}</div></div><div className="readiness"><div className="section-heading"><h2>Activation readiness</h2><LockKeyhole size={17} /></div><ul><li><Check size={16} /><span>Synthetic workspace available</span></li><li><span className="hollow-dot" /><span>Operator authentication unconfigured</span></li><li><span className="hollow-dot" /><span>Assessment connections not connected</span></li><li><span className="hollow-dot" /><span>Source and operation review unverified</span></li><li><span className="hollow-dot" /><span>Remediation allowlist empty</span></li></ul><button className="text-button" onClick={() => navigate("Coverage")}>Review coverage<ArrowRight size={15} /></button></div></section>
                        <section><div className="section-heading"><h2>Recent synthetic activity</h2><button className="text-button" onClick={() => navigate("Audit")}>Audit history<ArrowRight size={15} /></button></div><div className="activity-list">{filterAudit(data.audit, customer.tenantId).slice(0, 3).map((row) => <button key={row.id} onClick={() => setDetail({ kind: "audit", row })}><Activity size={17} /><time>{row.timestampUtc.slice(11, 16)} UTC</time><strong>{row.setting}</strong><Badge tone="neutral">{row.outcome}</Badge><ChevronRight size={15} /></button>)}</div></section>
                    </>}

                    {view !== "Overview" && <>
                        <div className="filter-toolbar"><div className="search-field"><Search size={17} /><input aria-label="Search records" placeholder={view === "Coverage" ? "Search service or control..." : "Search records..."} value={filters.search ?? ""} onChange={(event) => updateFilter("search", event.target.value)} /></div><label className="inline-filter"><ListFilter size={15} /><select aria-label="Service filter" value={filters.service ?? ""} onChange={(event) => updateFilter("service", event.target.value)}><option value="">All services</option>{data.services.map((service) => <option key={service.id}>{service.service}</option>)}</select></label>{(view === "Findings" || view === "Audit") && <select aria-label="Status filter" value={filters.status ?? ""} onChange={(event) => updateFilter("status", event.target.value)}><option value="">All {view === "Audit" ? "outcomes" : "statuses"}</option>{(view === "Audit" ? ["Intent", "Unknown", "Verified", "Blocked", "Failed", "Partial"] : STATUSES).map((status) => <option key={status}>{status}</option>)}</select>}<button className="icon-button" title="Clear filters" aria-label="Clear filters" onClick={() => setFilters({})}><X size={17} /></button></div>
                        {view === "Audit" && <div className="audit-filters"><label>From (UTC)<input type="date" aria-label="From UTC" value={filters.from ?? ""} onChange={(event) => updateFilter("from", event.target.value)} /></label><label>To (UTC)<input type="date" aria-label="To UTC" value={filters.to ?? ""} onChange={(event) => updateFilter("to", event.target.value)} /></label><label>Authorized user<input placeholder="Name or immutable ID" value={filters.user ?? ""} onChange={(event) => updateFilter("user", event.target.value)} /></label><label>Setting<input placeholder="Setting name" value={filters.setting ?? ""} onChange={(event) => updateFilter("setting", event.target.value)} /></label><label>Resource<input placeholder="Resource ID" value={filters.resource ?? ""} onChange={(event) => updateFilter("resource", event.target.value)} /></label></div>}

                        {view === "Findings" && <section><div className="table-caption"><span>{findings.length} of {tenantFindings.length} fixture findings</span><span>Current customer only</span></div>{findings.length ? <div className="table-scroll"><table><thead><tr><th><button className="sort-button" onClick={() => setSortAscending(!sortAscending)}>Control<ArrowUpDown size={13} /><span className="sr-only">{sortAscending ? "ascending" : "descending"}</span></button></th><th>Finding / service</th><th>Severity</th><th>Scenario status</th><th>Collected UTC</th><th><span className="sr-only">Detail</span></th></tr></thead><tbody>{findings.map((row) => <tr key={row.id}><td><code>{row.controlId}</code></td><td><button className="row-link" onClick={() => setDetail({ kind: "finding", row })}>{row.title}</button><span className="cell-secondary">{row.service}</span></td><td><span className={`severity ${row.severity.toLowerCase()}`}>{row.severity}</span></td><td><Badge tone={statusTone(row.status)}>{row.status}</Badge></td><td className="mono">{row.collectedAtUtc.slice(0, 10)}</td><td><button className="icon-button" aria-label={`Open ${row.controlId}`} title="Open finding detail" onClick={() => setDetail({ kind: "finding", row })}><ChevronRight size={16} /></button></td></tr>)}</tbody></table></div> : <Empty clear={() => setFilters({})} />}</section>}

                        {view === "Coverage" && <section><div className="table-caption"><span>{services.length} of 13 service families</span><span>0 automated / 0 write-enabled</span></div>{services.length ? <div className="table-scroll"><table><thead><tr><th>Service family</th><th>Control</th><th>Connection</th><th>Evidence method</th><th>Prerequisites / gaps</th></tr></thead><tbody>{services.map((service) => <tr key={service.id}><td><strong>{service.service}</strong></td><td><code>{service.id}</code></td><td><Badge>Not connected</Badge></td><td><Badge tone="amber">Manual / unverified</Badge></td><td><details aria-label="Review prerequisites"><summary>Review prerequisites</summary><div className="prerequisites"><p>{service.permissionPrerequisites.join(" ")}</p><p>{service.licensePrerequisites.join(" ")}</p><p>{service.runtimePrerequisites.join(" ")}</p><p>Cloud support: unverified. Remediation: unavailable. Source revision: unverified.</p></div></details></td></tr>)}</tbody></table></div> : <Empty clear={() => setFilters({})} />}</section>}

                        {view === "Assessments" && <section><div className="table-caption"><span>Synthetic history</span><span>Scheduling and live assessment unavailable</span></div>{findings.length ? <div className="table-scroll"><table><thead><tr><th>Assessment</th><th>Collected UTC</th><th>Scope</th><th>Provenance</th><th>State</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody><tr><td><strong>{tenantFindings[0].assessmentId}</strong><span className="cell-secondary">synthetic-ui-scenarios-v1</span></td><td className="mono">2026-01-01 00:00</td><td>{findings.length} / 13 fixture controls</td><td>Fixture only</td><td><Badge tone="amber">Not a live scan</Badge></td><td><button className="text-button" onClick={() => { setView("Findings"); }}>Review<ArrowRight size={15} /></button></td></tr></tbody></table></div> : <Empty clear={() => setFilters({})} />}<div className="notice assessment-notice"><LockKeyhole size={18} /><div><strong>Live assessment blocked</strong><p>Customer assignment, authentication, read connection and verified operation support are not configured. No job can be started from this preview.</p></div></div></section>}

                        {view === "Audit" && <section><div className="table-caption"><span>{audit.length} matching synthetic events</span><span>Append-only scenario / UTC</span></div>{audit.length ? <div className="table-scroll"><table className="audit-table"><thead><tr><th>Timestamp UTC / user</th><th>Service / setting</th><th>Previous</th><th>Requested</th><th>Verified</th><th>Outcome</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{audit.map((row) => <tr key={row.id}><td><time className="mono">{row.timestampUtc.replace("T", " ").replace("Z", "")}</time><span className="cell-secondary">{row.authorizedUser}</span></td><td><button className="row-link" onClick={() => setDetail({ kind: "audit", row })}>{row.setting}</button><span className="cell-secondary">{row.service}</span></td><td><code className="value-cell">{auditValueText(row.previous)}</code></td><td><code className="value-cell">{auditValueText(row.requested)}</code></td><td><code className="value-cell">{auditValueText(row.verified)}</code></td><td><Badge tone={row.outcome === "Verified" ? "green" : row.outcome === "Unknown" ? "amber" : "neutral"}>{row.outcome}</Badge></td><td><button className="icon-button" title="Open audit detail" aria-label={`Open ${row.id}`} onClick={() => setDetail({ kind: "audit", row })}><ChevronRight size={16} /></button></td></tr>)}</tbody></table></div> : <Empty clear={() => setFilters({})} />}</section>}
                    </>}
                    <footer className="workspace-footer"><span><LockKeyhole size={12} /> No live connections. No writes.</span><span>Synthetic evidence / not certification</span></footer>
                </main>
            </div>
            {detail && <DetailDrawer detail={detail} close={() => setDetail(null)} customerName={customer.name} />}
        </div>
    );
}