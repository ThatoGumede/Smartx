// Network Topology page: draws the mesh graph from /api/mesh/topology, traces routes, runs the
// failure lab, and queries the AVL-indexed history. All graph maths (BFS, Prim, Kruskal) happens in the API.
(function () {
    const POLL_MS = 3000;
    const W = 800, H = 520;
    const STATUS_COLOUR = { Normal: "#2e7d32", Warning: "#f9a825", Critical: "#c62828", Disconnected: "#757575", Gateway: "#0d47a1", Removed: "#9e9e9e" };

    const el = id => document.getElementById(id);
    const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const key = (a, b) => [a, b].sort().join("|");
    const note = t => { if (el("labNote")) el("labNote").textContent = t; };

    let topo = null;
    let selected = new URLSearchParams(location.search).get("node") || SmartXState.get("topology.selected", "");
    let route = null;

    // ---------- layout: gateway in the centre, one ring per hop count ----------
    function layout(nodes, edges) {
        const maxHop = Math.max(1, ...nodes.map(n => n.hops));
        const rx = (W / 2 - 70) / maxHop, ry = (H / 2 - 50) / maxHop;
        const cx = W / 2, cy = H / 2;
        const pos = {}, angle = {};

        const gw = nodes.find(n => n.isGateway);
        if (gw) { pos[gw.id] = { x: cx, y: cy }; angle[gw.id] = 0; }

        const parentOf = {};
        nodes.forEach(n => {
            if (n.hops < 2) return;
            const cands = edges.filter(e => (e.from === n.id || e.to === n.id))
                .map(e => ({ other: e.from === n.id ? e.to : e.from, cost: e.cost }))
                .filter(c => (nodes.find(x => x.id === c.other) || {}).hops === n.hops - 1)
                .sort((a, b) => a.cost - b.cost);
            if (cands.length) parentOf[n.id] = cands[0].other;
        });

        for (let hop = 1; hop <= maxHop; hop++) {
            const ring = nodes.filter(n => n.hops === hop).sort((a, b) => a.name.localeCompare(b.name));
            if (hop === 1) {
                ring.forEach((n, i) => { angle[n.id] = -Math.PI / 2 + (i * 2 * Math.PI) / ring.length; });
            } else {
                const groups = {};
                ring.forEach(n => (groups[parentOf[n.id] || ""] ||= []).push(n));
                Object.entries(groups).forEach(([p, kids]) => {
                    const base = angle[p] ?? 0;
                    kids.forEach((n, i) => { angle[n.id] = base + (i - (kids.length - 1) / 2) * 0.5; });
                });
            }
            const sorted = [...ring].sort((a, b) => angle[a.id] - angle[b.id]);
            const gap = Math.min(0.42, (2 * Math.PI / Math.max(sorted.length, 1)) * 0.9);
            for (let i = 1; i < sorted.length; i++)
                if (angle[sorted[i].id] < angle[sorted[i - 1].id] + gap) angle[sorted[i].id] = angle[sorted[i - 1].id] + gap;
            ring.forEach(n => { pos[n.id] = { x: cx + rx * hop * Math.cos(angle[n.id]), y: cy + ry * hop * Math.sin(angle[n.id]) }; });
        }

        const loose = nodes.filter(n => n.hops < 0);
        loose.forEach((n, i) => { pos[n.id] = { x: 60 + i * 110, y: H - 22 }; });
        return pos;
    }

    function routeEdgeKeys() {
        const set = new Set();
        const hops = route && (route.activeRoute || route.designedRoute);
        if (hops) for (let i = 1; i < hops.length; i++) set.add(key(hops[i - 1].id, hops[i].id));
        return set;
    }

    function drawMesh() {
        const svg = el("meshSvg");
        if (!svg || !topo) return;
        const pos = layout(topo.nodes, topo.edges);
        const hot = routeEdgeKeys();
        const parts = [];

        const edgeStyle = e => {
            if (hot.has(key(e.from, e.to))) return `stroke="#fd7e14" stroke-width="6" opacity="0.95"`;
            if (!e.active) return `stroke="#c62828" stroke-width="1.5" stroke-dasharray="5 4" opacity="0.7"`;
            if (e.inRoutingTree) return `stroke="#0d6efd" stroke-width="3.5"`;
            return `stroke="#b0b7bf" stroke-width="1.2"`;
        };

        [...topo.edges].sort((a, b) => (hot.has(key(a.from, a.to)) ? 1 : 0) - (hot.has(key(b.from, b.to)) ? 1 : 0)).forEach(e => {
            const a = pos[e.from], b = pos[e.to];
            if (!a || !b) return;
            parts.push(`<line x1="${a.x.toFixed(1)}" y1="${a.y.toFixed(1)}" x2="${b.x.toFixed(1)}" y2="${b.y.toFixed(1)}" ${edgeStyle(e)}><title>${esc(e.from)} - ${esc(e.to)}: cost ${e.cost}, RSSI ${e.rssi} dBm</title></line>`);
            if (e.inRoutingTree && e.active)
                parts.push(`<text x="${((a.x + b.x) / 2).toFixed(1)}" y="${((a.y + b.y) / 2 - 4).toFixed(1)}" class="edge-cost">${e.cost}</text>`);
        });

        topo.nodes.forEach(n => {
            const p = pos[n.id];
            if (!p) return;
            const colour = STATUS_COLOUR[n.status] || "#9e9e9e";
            const sel = selected && (n.id.toLowerCase() === selected.toLowerCase() || n.name.toLowerCase() === selected.toLowerCase() || n.sensorId === selected);
            const isolated = n.alive && !n.reachable && !n.isGateway;
            const shape = n.isGateway
                ? `<rect x="${p.x - 17}" y="${p.y - 17}" width="34" height="34" rx="6" fill="${colour}"/>`
                : `<circle cx="${p.x.toFixed(1)}" cy="${p.y.toFixed(1)}" r="${sel ? 19 : 15}" fill="${colour}" stroke="${sel ? "#fd7e14" : isolated ? "#c62828" : "#ffffff"}" stroke-width="${sel || isolated ? 4 : 2}" ${isolated ? 'stroke-dasharray="4 3"' : ""}/>`;
            parts.push(`<g class="mesh-node" data-id="${esc(n.id)}" data-name="${esc(n.name)}"><title>${esc(n.name)} - ${esc(n.status)}${n.hops >= 0 ? `, ${n.hops} hop(s) in the design` : ""}</title>${shape}
                <text x="${p.x.toFixed(1)}" y="${(p.y + (n.isGateway ? 32 : 31)).toFixed(1)}" class="node-label" text-anchor="middle">${esc(n.name)}</text></g>`);
        });

        svg.innerHTML = parts.join("");
    }

    function renderSummary() {
        const r = topo.routing;
        const chip = (label, value, cls = "bg-light text-dark") => `<span class="badge ${cls} border fs-6 fw-normal">${label}: <strong>${value}</strong></span>`;
        el("summary").innerHTML =
            chip("Nodes", topo.nodes.length - 1) + chip("Links", topo.edges.length) +
            chip("Reaching gateway", topo.reachableCount, "bg-success-subtle text-success-emphasis") +
            chip("Down", topo.down.length, topo.down.length ? "bg-danger-subtle text-danger-emphasis" : "bg-light text-dark") +
            chip("Isolated", topo.isolated.length, topo.isolated.length ? "bg-warning-subtle text-warning-emphasis" : "bg-light text-dark") +
            chip("Routing tree cost", r.activeCost) +
            chip("Prim = Kruskal", r.verified ? "verified" : "MISMATCH", r.verified ? "bg-light text-dark" : "bg-danger text-white") +
            (Math.abs(r.activeCost - r.designedCost) > 0.01 ? chip("Designed cost", r.designedCost) : "");

        el("nodeList").innerHTML = topo.nodes.filter(n => !n.isGateway)
            .map(n => `<option value="${esc(n.name)}">${esc(n.id)}</option>`).join("");
    }

    const hopChips = hops => hops.map((h, i) =>
        `<span class="hop ${h.alive ? "" : "hop-dead"}">${esc(h.name)}</span>${i < hops.length - 1 ? `<span class="hop-cost">${hops[i + 1].costFromPrevious} &rarr;</span>` : ""}`).join("");

    function renderRoute() {
        const box = el("routeResult");
        if (!box) return;
        if (!route) { box.innerHTML = "Enter a device to see how its data reaches the gateway."; return; }

        const tone = !route.alive || !route.reachable ? "alert-danger" : route.rerouted ? "alert-warning" : "alert-success";
        let html = `<div class="alert ${tone} py-2 mb-2">${esc(route.advice)}</div>`;
        if (route.activeRoute) html += `<div class="mb-2"><strong>Active route</strong> (cost ${route.activeCost})<div class="hops">${hopChips(route.activeRoute)}</div></div>`;
        if (route.designedRoute && (route.rerouted || !route.alive))
            html += `<div class="mb-2"><strong>Designed route</strong> (cost ${route.designedCost})<div class="hops">${hopChips(route.designedRoute)}</div></div>`;
        if (route.fewestHopsRoute) html += `<div class="mb-2 text-secondary"><strong>Fewest hops</strong> (breadth-first): ${route.fewestHopsRoute.map(h => esc(h.name)).join(" &rarr; ")}</div>`;
        if (route.affected.length)
            html += `<div><strong>Nodes that relied on it</strong><ul class="mb-0">${route.affected.map(a =>
                `<li>${esc(a.name)}: ${a.newRoute ? a.newRoute.map(h => esc(h.name)).join(" &rarr; ") + ` (cost ${a.newCost})` : "<span class='text-danger'>no route</span>"}</li>`).join("")}</ul></div>`;
        box.innerHTML = html;
    }

    function renderConnectivity(fromGateway, fromNode) {
        const box = el("connectivity");
        if (!box) return;
        const list = c => c.connected.length ? c.connected.map(n => `<span class="badge bg-secondary me-1 mb-1">${esc(n.name)} &middot; ${n.hops} hop${n.hops === 1 ? "" : "s"}</span>`).join("") : "<span class='text-secondary'>none</span>";
        let html = `<div class="mb-2"><strong>Reachable from the gateway (${fromGateway.connected.length})</strong><div>${list(fromGateway)}</div></div>
            <div class="mb-2"><strong>Down (${fromGateway.down.length})</strong> ${fromGateway.down.map(n => `<span class="badge bg-danger me-1">${esc(n)}</span>`).join("") || "<span class='text-secondary'>none</span>"}</div>
            <div class="mb-2"><strong>Up but cut off (${fromGateway.isolated.length})</strong> ${fromGateway.isolated.map(n => `<span class="badge bg-warning text-dark me-1">${esc(n)}</span>`).join("") || "<span class='text-secondary'>none</span>"}</div>`;
        if (fromNode) html += `<div><strong>Subnet seen from ${esc(fromNode.from)} (${fromNode.connected.length})</strong><div>${list(fromNode)}</div></div>`;
        box.innerHTML = html;
    }

    async function poll() {
        if (!el("meshSvg")) return;
        try {
            const [t, gw] = await Promise.all([SmartXApi.get("/api/mesh/topology"), SmartXApi.get("/api/mesh/connected")]);
            let r = null, sub = null;
            if (selected) {
                try { [r, sub] = await Promise.all([SmartXApi.get("/api/mesh/route/" + encodeURIComponent(selected)), SmartXApi.get("/api/mesh/connected?from=" + encodeURIComponent(selected))]); }
                catch { r = null; }
            }
            if (!el("meshSvg")) return;
            topo = t; route = r;
            renderSummary(); drawMesh(); renderRoute(); renderConnectivity(gw, sub);
            el("lastPoll").textContent = new Date().toLocaleTimeString();
            el("apiError").classList.add("d-none");
        } catch (err) {
            if (!el("apiError")) return;
            el("apiError").textContent = `Could not reach the API at ${SmartXApi.base()}: ${err.message}`;
            el("apiError").classList.remove("d-none");
        }
    }

    function select(value) {
        selected = value || "";
        SmartXState.set("topology.selected", selected);
        el("nodeInput").value = selected;
        poll();
        if (selected) runQuery();
    }

    // ---------- historical log (AVL range query) ----------
    const toLocalInput = d => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    function setRange(hours) {
        const to = new Date();
        el("logTo").value = toLocalInput(to);
        el("logFrom").value = toLocalInput(new Date(to.getTime() - hours * 3600000));
    }

    function drawChart(readings) {
        const svg = el("logChart");
        const values = readings.map(r => r.numericValue ?? (r.boolValue ? 1 : 0));
        if (values.length < 2) { svg.classList.add("d-none"); return; }
        const min = Math.min(...values), max = Math.max(...values), span = max - min || 1;
        const pts = values.map((v, i) => `${(i / (values.length - 1) * 600).toFixed(1)},${(100 - ((v - min) / span) * 90).toFixed(1)}`).join(" ");
        svg.innerHTML = `<polyline points="${pts}" fill="none" stroke="#0d6efd" stroke-width="1.8" vector-effect="non-scaling-stroke"/>
            <text x="4" y="12" class="chart-label">${max.toFixed(1)}</text><text x="4" y="106" class="chart-label">${min.toFixed(1)}</text>`;
        svg.classList.remove("d-none");
    }

    async function runQuery() {
        if (!selected || !el("logRows")) return;
        const from = new Date(el("logFrom").value), to = new Date(el("logTo").value);
        if (isNaN(from) || isNaN(to)) { el("logSummary").textContent = "Pick a valid from and to time."; return; }
        try {
            const qs = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), take: "500" });
            const res = await SmartXApi.get(`/api/history/${encodeURIComponent(selected)}/range?${qs}`);
            if (!el("logRows")) return;
            const r = res.result;
            el("logTitle").textContent = res.name;
            const stats = r.min !== null ? ` &middot; min ${r.min} &middot; max ${r.max} &middot; avg ${r.average}` : r.onCount !== null ? ` &middot; ON in ${r.onCount} readings` : "";
            el("logSummary").innerHTML = `<strong>${r.count}</strong> readings in range${r.truncated ? ` (showing the latest ${r.readings.length})` : ""}${stats}<br>
                <span class="text-secondary">Index: visited ${r.nodesVisited} of ${r.totalStored} stored nodes &middot; tree height ${r.treeHeight} (minimum possible ${r.minimumHeight}) &middot; ${r.elapsedMs} ms</span>`;
            drawChart(r.readings);
            el("logRows").innerHTML = [...r.readings].reverse().map(x =>
                `<tr><td>${new Date(x.timestamp).toLocaleString()}</td><td>${esc(x.displayValue)}${esc(x.unit === "state" ? "" : x.unit)}</td></tr>`).join("")
                || `<tr><td colspan="2" class="text-secondary">No readings in this range.</td></tr>`;
        } catch (err) {
            el("logSummary").textContent = err.message;
        }
    }

    // ---------- wiring ----------
    setRange(24);
    el("nodeInput").value = selected;
    el("btnTrace").addEventListener("click", () => select(el("nodeInput").value.trim()));
    el("nodeInput").addEventListener("keydown", e => { if (e.key === "Enter") select(el("nodeInput").value.trim()); });
    el("meshSvg").addEventListener("click", e => {
        const g = e.target.closest(".mesh-node");
        if (g && g.dataset.name !== "Gateway") select(g.dataset.id);
        else if (g) select("GATEWAY");
    });
    el("topoRoot").addEventListener("click", e => {
        const b = e.target.closest("[data-range]");
        if (b) { setRange(Number(b.dataset.range)); runQuery(); }
    });
    el("btnQuery").addEventListener("click", runQuery);

    const fault = async method => {
        if (!selected || selected === "GATEWAY") { note("Select a node first."); return; }
        try {
            const res = await fetch(`${SmartXApi.base()}/api/simulation/faults/${encodeURIComponent(selected)}`, { method });
            if (!res.ok) throw new Error(await res.text());
            note(method === "POST" ? "Node silenced. The gateway will mark it down in about 12 seconds." : "Node restored. It will reconnect on its next report.");
            poll();
        } catch (err) { note(err.message); }
    };
    el("btnFail").addEventListener("click", () => fault("POST"));
    el("btnRestore").addEventListener("click", () => fault("DELETE"));

    poll();
    if (selected) runQuery();
    SmartXPage.every(poll, POLL_MS);
})();
