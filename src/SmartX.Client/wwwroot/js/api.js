// Thin fetch() wrapper every page uses to talk to the SmartX.Api gateway. window.SMARTX_API_BASE
// is injected by _Layout.cshtml from appsettings.json ("ApiBaseUrl"), so this file never
// hard-codes a host.
const SmartXApi = {
    base() {
        return window.SMARTX_API_BASE || "http://localhost:5050";
    },

    async get(path) {
        const res = await fetch(`${this.base()}${path}`);
        if (!res.ok) throw new Error(`GET ${path} failed: ${res.status}`);
        return res.json();
    },

    async post(path, body) {
        const res = await fetch(`${this.base()}${path}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        });
        if (!res.ok) throw new Error(`POST ${path} failed: ${res.status}`);
        return res.json();
    },

    async postForm(path, formData) {
        const res = await fetch(`${this.base()}${path}`, { method: "POST", body: formData });
        if (!res.ok) throw new Error(`POST ${path} failed: ${res.status}`);
        return res.json();
    },
};
