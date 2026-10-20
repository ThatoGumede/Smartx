// Real-Time Command Stream page. Polls the API every 2s for devices (filtered server-side),
// pipeline stats, alerts, command history and insights, and issues override commands.
(function () {
    const POLL_MS = 2000;
    const saved = SmartXState.get("stream.filters", {});
    const filters = { q: saved.q || "", category: saved.category || "", status: saved.status || "" };
    let lastStats = null;
    let searchTimer, logTimer;

    const el = id => document.getElementById(id);
    const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const clock = iso => iso ? new Date(iso).toLocaleTimeString() : "never";
    const note = text => { if (el("labNote")) el("labNote").textContent = text; };
    const persist = () => SmartXState.set("stream.filters", filters);

    function sparkline(series) {
        if (!series || series.length < 2) return "";
        const min = Math.min(...series), max = Math.max(...series), span = max - min || 1;
        const step = 120 / (series.length - 1);
        const pts = series.map((v, i) => `${(i * step).toFixed(1)},${(26 - ((v - min) / span) * 24).toFixed(1)}`).join(" ");
        return `<svg class="spark" viewBox="0 0 120 28" preserveAspectRatio="none"><polyline points="${pts}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/></svg>`;
    }

    function renderDevices(devices) {
        el("deviceCount").textContent = `${devices.length} device(s) shown`;
        const grid = el("deviceGrid");
        if (!devices.length) {
            grid.innerHTML = `<p class="text-secondary">No devices match the current filters.</p>`;
            return;
        }
        grid.innerHTML = devices.map(d => {
            const actions = d.actuatorOn === null ? "" : `
                <div class="tile-actions">
                    <button class="btn btn-sm ${d.actuatorOn ? "btn-light" : "btn-outline-light"}" data-cmd="ON" data-id="${esc(d.sensorId)}">ON</button>
                    <button class="btn btn-sm ${d.actuatorOn ? "btn-outline-light" : "btn-light"}" data-cmd="OFF" data-id="${esc(d.sensorId)}">OFF</button>
                </div>`;
            return `
                <div class="sensor-tile status-${d.status} ${d.status !== "Disconnected" ? "pulsing" : ""} ${d.flagged ? "flagged" : ""}">
                    ${d.flagged ? `<span class="flag-chip">Flagged by insights</span>` : ""}
                    <div class="label fw-semibold">${esc(d.name)}</div>
                    <div class="label">${esc(d.location)}</div>
                    <div class="value">${esc(d.latestValue)}</div>
                    ${sparkline(d.series)}
                    <div class="meta">${esc(d.category)} &middot; ${d.status} &middot; ${clock(d.lastSeen)}</div>
                    ${actions}
                </div>`;
        }).join("");
    }

    function renderInsights(ins) {
        el("suggestions").innerHTML = ins.suggestions.length
            ? ins.suggestions.map(s => `
                <div class="suggestion kind-${esc(s.kind)}">
                    <div class="d-flex justify-content-between gap-2"><strong>${esc(s.title)}</strong><span class="badge bg-secondary">${esc(s.kind)}</span></div>
                    <div class="small text-secondary">${esc(s.detail)}</div>
                    ${s.commandAction ? `<button class="btn btn-sm btn-primary mt-1" data-cmd="${esc(s.commandAction)}" data-id="${esc(s.sensorId)}">Apply</button>` : ""}
                </div>`).join("")
            : `<p class="small text-secondary mb-0">Nothing to flag right now.</p>`;

        el("patterns").innerHTML = ins.patterns.length
            ? "Learned: " + ins.patterns.map(p => `${esc(p.trigger)} &rarr; ${esc(p.target)} (${p.count}&times;, ${Math.round(p.confidence * 100)}%)`).join("; ")
            : "No patterns learned yet. Search for a device or send a command shortly after an alert and the engine starts learning.";
    }

    function renderHistory(h) {
        el("undoDepth").textContent = `${h.undoDepth} undoable`;
        el("btnUndo").disabled = h.undoDepth === 0;
        el("history").innerHTML = h.commands.length
            ? h.commands.map(c => `<div class="${c.undone ? "text-decoration-line-through text-secondary" : ""}">${clock(c.issuedAt)} &middot; ${esc(c.description)}</div>`).join("")
            : `<span class="text-secondary">No commands issued yet.</span>`;
    }

    function renderAlerts(a) {
        el("suppressed").textContent = `${a.suppressedDuplicates} duplicate(s) suppressed`;
        const colour = { Critical: "danger", Warning: "warning", Disconnected: "secondary" };
        el("activeAlerts").innerHTML = a.active.length
            ? a.active.map(x => `<span class="badge bg-${colour[x.kind] || "secondary"} me-1 mb-1">${esc(x.label)}: ${esc(x.kind)}</span>`).join("")
            : `<span class="text-secondary">No active alerts.</span>`;
        el("alertEvents").innerHTML = a.events.map(e => `<div>${clock(e.at)} &middot; <strong>${esc(e.kind)}</strong> ${esc(e.message)}</div>`).join("");
    }

    function renderPipeline(s) {
        lastStats = s;
        el("pipelineState").innerHTML = s.paused ? `<span class="badge bg-warning text-dark">PAUSED</span>` : `<span class="badge bg-success">running</span>`;
        el("btnPause").textContent = s.paused ? "Resume processing" : "Pause processing";
        el("pipelineCounts").innerHTML = `
            <div>Standard queue: <strong>${s.standardDepth}</strong></div>
            <div>Priority queue: <strong>${s.priorityDepth}</strong></div>
            <div>Done: <strong>${s.processedStandard}</strong> std / <strong>${s.processedPriority}</strong> prio</div>`;
        el("recentProcessed").innerHTML = s.recent.map(r => `
            <div>#${r.order} <span class="badge lane-${esc(r.lane)}">${esc(r.lane)}</span> ${esc(r.label)} ${esc(r.value)} <span class="text-secondary">(${r.waitedMs} ms)</span></div>`).join("");
    }

    async function poll() {
        if (!el("deviceGrid")) return;
        try {
            const qs = new URLSearchParams();
            if (filters.q) qs.set("q", filters.q);
            if (filters.category) qs.set("category", filters.category);
            if (filters.status) qs.set("status", filters.status);

            const [devices, stats, alerts, history, insights] = await Promise.all([
                SmartXApi.get("/api/stream/snapshot?" + qs),
                SmartXApi.get("/api/pipeline/stats"),
                SmartXApi.get("/api/alerts"),
                SmartXApi.get("/api/commands/history"),
                SmartXApi.get("/api/insights"),
            ]);
            if (!el("deviceGrid")) return;

            renderDevices(devices);
            renderPipeline(stats);
            renderAlerts(alerts);
            renderHistory(history);
            renderInsights(insights);
            el("lastPoll").textContent = new Date().toLocaleTimeString();
            el("apiError").classList.add("d-none");
        } catch (err) {
            if (!el("apiError")) return;
            el("apiError").textContent = `Could not reach the API at ${SmartXApi.base()}: ${err.message}`;
            el("apiError").classList.remove("d-none");
        }
    }

    async function run(fn, success) {
        try { await fn(); if (success) note(success); }
        catch (err) { note(err.message); }
        poll();
    }

    async function firstDevice(category) {
        const all = await SmartXApi.get("/api/sensors");
        return all.find(d => d.category === category);
    }

    // ---- filters ----
    el("searchBox").value = filters.q;
    el("categoryFilter").value = filters.category;
    el("statusFilter").value = filters.status;

    el("searchBox").addEventListener("input", e => {
        filters.q = e.target.value; persist();
        clearTimeout(searchTimer); searchTimer = setTimeout(poll, 250);
        // Tell the engine what the operator looked for once they stop typing: one of its learning signals.
        clearTimeout(logTimer);
        logTimer = setTimeout(() => {
            if (filters.q.trim().length >= 2) SmartXApi.post("/api/activity/search", { term: filters.q }).catch(() => { });
        }, 1200);
    });
    el("categoryFilter").addEventListener("change", e => { filters.category = e.target.value; persist(); poll(); });
    el("statusFilter").addEventListener("change", e => { filters.status = e.target.value; persist(); poll(); });
    el("clearFilters").addEventListener("click", () => {
        filters.q = filters.category = filters.status = ""; persist();
        el("searchBox").value = ""; el("categoryFilter").value = ""; el("statusFilter").value = "";
        poll();
    });

    // ---- commands (tiles and suggestion buttons share data-cmd / data-id) ----
    el("streamRoot").addEventListener("click", e => {
        const b = e.target.closest("[data-cmd]");
        if (b) run(() => SmartXApi.post("/api/commands", { sensorId: b.dataset.id, action: b.dataset.cmd }));
    });
    el("btnAllOff").addEventListener("click", () => run(() => SmartXApi.post("/api/commands/all", { action: "OFF" })));
    el("btnAllOn").addEventListener("click", () => run(() => SmartXApi.post("/api/commands/all", { action: "ON" })));
    el("btnUndo").addEventListener("click", () => run(() => SmartXApi.post("/api/commands/undo", {})));

    // ---- test lab: these hit the API exactly as a device would ----
    el("btnPause").addEventListener("click", () =>
        run(() => SmartXApi.post(lastStats && lastStats.paused ? "/api/pipeline/resume" : "/api/pipeline/pause", {})));

    el("btnFlood").addEventListener("click", () => run(async () => {
        const d = await firstDevice("Environmental");
        if (!d) throw new Error("Register an environmental sensor first.");
        await Promise.all(Array.from({ length: 30 }, (_, i) =>
            SmartXApi.post("/api/ingest", { macAddress: d.macAddress, dataType: "float", number: 40 + (i % 10), unit: "%" })));
    }, "Queued 30 routine readings. If processing is paused they are waiting in the standard queue."));

    el("btnCritical").addEventListener("click", () => run(async () => {
        const power = await firstDevice("PowerConsumption");
        const d = power || await firstDevice("Environmental");
        if (!d) throw new Error("Register a power or environmental sensor first.");
        const payload = power
            ? { macAddress: d.macAddress, dataType: "int", number: 3100, unit: "W" }
            : { macAddress: d.macAddress, dataType: "float", number: 4, unit: "%" };
        const r = await SmartXApi.post("/api/ingest", payload);
        note(`Critical reading for ${d.name || d.macAddress} went to the ${r.lane} lane.`);
    }));

    el("btnSeed").addEventListener("click", () => run(async () => {
        const r = await SmartXApi.post("/api/insights/seed-demo", {});
        note(r.message + " Now trigger a Moisture Sensor B alert to see the suggestion.");
    }));

    poll();
    SmartXPage.every(poll, POLL_MS);
    SmartXPage.onLeave(() => { clearTimeout(searchTimer); clearTimeout(logTimer); });
})();
