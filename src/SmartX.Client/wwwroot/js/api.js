// Thin fetch() wrapper every page uses to talk to the SmartX.Api gateway. window.SMARTX_API_BASE
// is injected by _Layout.cshtml from appsettings.json ("ApiBaseUrl"), so this file never
// hard-codes a host.
const SmartXApi = {
    base() {
        return window.SMARTX_API_BASE || "http://localhost:5050";
    },

    async fail(res, method, path) {
        let detail = "";
        try { detail = (await res.text()).slice(0, 200); } catch { }
        throw new Error(detail || `${method} ${path} failed: ${res.status}`);
    },

    async get(path) {
        const res = await fetch(`${this.base()}${path}`);
        if (!res.ok) await this.fail(res, "GET", path);
        return res.json();
    },

    async post(path, body) {
        const res = await fetch(`${this.base()}${path}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body ?? {}),
        });
        if (!res.ok) await this.fail(res, "POST", path);
        const text = await res.text();
        return text ? JSON.parse(text) : {};
    },

    async postForm(path, formData) {
        const res = await fetch(`${this.base()}${path}`, { method: "POST", body: formData });
        if (!res.ok) await this.fail(res, "POST", path);
        return res.json();
    },
};
