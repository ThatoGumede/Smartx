// Part 1 live telemetry dashboard: polls /api/dashboard/state and paints each sensor as a coloured,
// pulsing tile. The API computes the status; this file only maps it to a colour and a pulse.
(function () {
    const POLL_INTERVAL_MS = 3000;
    const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

    function renderTiles(snapshots) {
        const grid = document.getElementById("tileGrid");
        if (!grid) return;

        if (snapshots.length === 0) {
            grid.innerHTML = `<p class="text-secondary">No sensors registered yet. Go to <a href="/Sensors">Sensor Ingestion</a> to add one, or start the device simulator.</p>`;
            return;
        }

        grid.innerHTML = snapshots.map(s => {
            const pulsing = s.status !== "Disconnected" ? "pulsing" : "";
            const lastSeen = s.lastSeen ? new Date(s.lastSeen).toLocaleTimeString() : "never";
            return `
                <div class="sensor-tile status-${s.status} ${pulsing}" title="${esc(s.displayLabel)}">
                    <div class="label">${esc(s.displayLabel)}</div>
                    <div class="value">${esc(s.latestValue)}</div>
                    <div class="meta">${s.status} &middot; last seen ${lastSeen}</div>
                </div>`;
        }).join("");
    }

    async function pollDashboard() {
        if (!document.getElementById("tileGrid")) return;
        try {
            const snapshots = await SmartXApi.get("/api/dashboard/state");
            if (!document.getElementById("tileGrid")) return;
            renderTiles(snapshots);
            document.getElementById("lastPoll").textContent = new Date().toLocaleTimeString();
        } catch (err) {
            const grid = document.getElementById("tileGrid");
            if (grid) grid.innerHTML = `<p class="text-danger">Could not reach the API at ${esc(SmartXApi.base())}: ${esc(err.message)}</p>`;
        }
    }

    pollDashboard();
    SmartXPage.every(pollDashboard, POLL_INTERVAL_MS);
})();
