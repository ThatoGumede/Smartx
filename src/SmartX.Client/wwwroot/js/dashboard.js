// Live pre-attentive telemetry dashboard. Polls /api/dashboard/state every 3s and paints
// each sensor as a coloured, pulsing tile - the API has already computed Status (Normal /
// Warning / Critical / Disconnected), so this file does no threshold logic of its own; it
// only maps a status string to a colour band and a pulse rate.

const POLL_INTERVAL_MS = 3000;

function renderTiles(snapshots) {
    const grid = document.getElementById("tileGrid");

    if (snapshots.length === 0) {
        grid.innerHTML = `<p class="text-secondary">No sensors registered yet. Go to <a href="/Sensors">Sensor Ingestion</a> to add one.</p>`;
        return;
    }

    grid.innerHTML = snapshots.map(s => {
        const pulsing = s.status !== "Disconnected" ? "pulsing" : "";
        const lastSeen = s.lastSeen ? new Date(s.lastSeen).toLocaleTimeString() : "never";
        return `
            <div class="sensor-tile status-${s.status} ${pulsing}" title="${s.displayLabel}">
                <div class="label">${s.displayLabel}</div>
                <div class="value">${s.latestValue}</div>
                <div class="meta">${s.status} &middot; last seen ${lastSeen}</div>
            </div>
        `;
    }).join("");
}

async function pollDashboard() {
    try {
        const snapshots = await SmartXApi.get("/api/dashboard/state");
        renderTiles(snapshots);
        document.getElementById("lastPoll").textContent = new Date().toLocaleTimeString();
    } catch (err) {
        document.getElementById("tileGrid").innerHTML =
            `<p class="text-danger">Could not reach the API at ${SmartXApi.base()}: ${err.message}</p>`;
    }
}

pollDashboard();
setInterval(pollDashboard, POLL_INTERVAL_MS);
