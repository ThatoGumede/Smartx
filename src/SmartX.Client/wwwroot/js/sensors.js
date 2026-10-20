(function () {
// Sensor console: registration, live listing, media attachment, and recursive
// deployment-tree validation. Everything here talks to SmartX.Api via SmartXApi (api.js).

const SAMPLE_TREE = {
    name: "Facility A",
    tier: "Facility",
    children: [
        {
            name: "Zone 1",
            tier: "Zone",
            children: [
                {
                    name: "Sub-Zone B",
                    tier: "SubZone",
                    children: [
                        { name: "Node 4", tier: "Node", hasSensorAssigned: true },
                        { name: "Node 5", tier: "Node", hasSensorAssigned: true },
                    ],
                },
            ],
        },
    ],
};

const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

function categoryBadge(category) {
    const colors = { Environmental: "success", PowerConsumption: "warning", Actuator: "info" };
    return `<span class="badge bg-${colors[category] || "secondary"}">${category}</span>`;
}

async function loadSensors() {
    const body = document.getElementById("sensorTableBody");
    try {
        const sensors = await SmartXApi.get("/api/sensors");
        if (sensors.length === 0) {
            body.innerHTML = `<tr><td colspan="5" class="text-center text-secondary py-3">No sensors registered yet.</td></tr>`;
            return;
        }

        body.innerHTML = sensors.map(s => `
            <tr>
                <td class="small"><strong>${esc(s.displayName)}</strong><br>${esc(s.deploymentLocation)}</td>
                <td>${categoryBadge(s.category)}</td>
                <td class="small font-monospace">${esc(s.macAddress)}</td>
                <td class="small">${s.attachments.length} file(s)</td>
                <td>
                    <form class="d-flex gap-1 upload-form" data-sensor-id="${s.id}">
                        <input type="file" class="form-control form-control-sm" required>
                        <button type="submit" class="btn btn-sm btn-outline-primary">Attach</button>
                    </form>
                </td>
            </tr>
        `).join("");

        document.querySelectorAll(".upload-form").forEach(form => {
            form.addEventListener("submit", onUploadSubmit);
        });
    } catch (err) {
        body.innerHTML = `<tr><td colspan="5" class="text-danger py-3">Could not reach the API: ${esc(err.message)}</td></tr>`;
    }
}

async function onUploadSubmit(evt) {
    evt.preventDefault();
    const form = evt.currentTarget;
    const sensorId = form.dataset.sensorId;
    const fileInput = form.querySelector("input[type=file]");
    if (!fileInput.files.length) return;

    const formData = new FormData();
    formData.append("file", fileInput.files[0]);

    const btn = form.querySelector("button");
    btn.disabled = true;
    btn.textContent = "Uploading...";
    try {
        await SmartXApi.postForm(`/api/sensors/${sensorId}/media`, formData);
        await loadSensors();
    } catch (err) {
        alert(`Upload failed: ${err.message}`);
        btn.disabled = false;
        btn.textContent = "Attach";
    }
}

document.getElementById("registerForm").addEventListener("submit", async (evt) => {
    evt.preventDefault();
    const status = document.getElementById("registerStatus");
    const payload = {
        name: document.getElementById("sensorName").value.trim(),
        macAddress: document.getElementById("macAddress").value.trim(),
        deploymentLocation: document.getElementById("deploymentLocation").value.trim(),
        category: document.getElementById("category").value,
    };

    if (!payload.macAddress || !payload.deploymentLocation) {
        status.textContent = "MAC address and location are required.";
        status.className = "ms-2 small text-danger";
        return;
    }

    try {
        await SmartXApi.post("/api/sensors", payload);
        status.textContent = "Registered. Mock telemetry will begin within a few seconds.";
        status.className = "ms-2 small text-success";
        evt.target.reset();
        await loadSensors();
    } catch (err) {
        status.textContent = `Failed: ${err.message}`;
        status.className = "ms-2 small text-danger";
    }
});

document.getElementById("refreshSensorsBtn").addEventListener("click", loadSensors);

document.getElementById("loadSampleBtn").addEventListener("click", () => {
    document.getElementById("deploymentTree").value = JSON.stringify(SAMPLE_TREE, null, 2);
});

document.getElementById("validateBtn").addEventListener("click", async () => {
    const resultDiv = document.getElementById("validationResult");
    let tree;
    try {
        tree = JSON.parse(document.getElementById("deploymentTree").value);
    } catch {
        resultDiv.innerHTML = `<div class="alert alert-danger py-2">That isn't valid JSON.</div>`;
        return;
    }

    try {
        const result = await SmartXApi.post("/api/deployment/validate", tree);
        if (result.isValid) {
            resultDiv.innerHTML = `<div class="alert alert-success py-2">Valid deployment tree - every node matched its expected tier.</div>`;
        } else {
            resultDiv.innerHTML = `<div class="alert alert-warning py-2"><strong>${result.errors.length} issue(s):</strong><ul class="mb-0">${result.errors.map(e => `<li>${e}</li>`).join("")}</ul></div>`;
        }
    } catch (err) {
        resultDiv.innerHTML = `<div class="alert alert-danger py-2">Validation request failed: ${err.message}</div>`;
    }
});

document.getElementById("deploymentTree").value = JSON.stringify(SAMPLE_TREE, null, 2);
loadSensors();

})();
