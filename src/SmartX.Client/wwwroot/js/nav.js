// App shell: persistent state + asynchronous page navigation.
//
// Clicking a same-site link fetches the target page in the background and swaps only the <main>
// content. The navbar, the window and SmartXState are never torn down, so nothing the operator was
// doing is lost between modules, and the API is not hit with a fresh page load each time. If anything
// goes wrong the code falls back to an ordinary navigation, so links always work.
(function () {
    const KEY = "smartx.state";
    const read = () => { try { return JSON.parse(sessionStorage.getItem(KEY)) || {}; } catch { return {}; } };

    // Small key/value store for UI state (filters, selections). Backed by sessionStorage so it also
    // survives a manual refresh.
    window.SmartXState = {
        get(name, fallback) { const v = read()[name]; return v === undefined ? fallback : v; },
        set(name, value) { const s = read(); s[name] = value; try { sessionStorage.setItem(KEY, JSON.stringify(s)); } catch { } },
    };

    // Pages register their timers here so leaving a page stops its polling.
    const cleanups = [];
    window.SmartXPage = {
        onLeave(fn) { cleanups.push(fn); },
        every(fn, ms) { const id = setInterval(fn, ms); cleanups.push(() => clearInterval(id)); return id; },
    };

    function setActive() {
        const path = location.pathname.toLowerCase();
        document.querySelectorAll(".navbar .nav-link[data-path]").forEach(a =>
            a.classList.toggle("active", a.dataset.path.toLowerCase() === path));
    }

    async function navigate(url, push) {
        try {
            const res = await fetch(url, { headers: { "X-Requested-With": "SmartX-Nav" } });
            if (!res.ok) throw new Error("HTTP " + res.status);

            const doc = new DOMParser().parseFromString(await res.text(), "text/html");
            const incoming = doc.querySelector("main[role=main]");
            if (!incoming) throw new Error("No main element");

            cleanups.splice(0).forEach(fn => { try { fn(); } catch { } });
            document.querySelectorAll("script[data-page-script]").forEach(s => s.remove());
            document.querySelector("main[role=main]").innerHTML = incoming.innerHTML;
            document.title = doc.title;
            if (push) history.pushState({}, "", url);
            setActive();
            window.scrollTo(0, 0);

            doc.querySelectorAll("script[data-page-script]").forEach(old => {
                const s = document.createElement("script");
                s.src = old.getAttribute("src");
                s.setAttribute("data-page-script", "");
                document.body.appendChild(s);
            });
        } catch {
            location.href = url;
        }
    }

    document.addEventListener("click", e => {
        const a = e.target.closest("a[href]");
        if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if ((a.target && a.target !== "_self") || a.hasAttribute("download") || a.origin !== location.origin) return;
        if (a.pathname === location.pathname && a.hash) return;
        e.preventDefault();
        if (a.href !== location.href) navigate(a.href, true);
    });

    window.addEventListener("popstate", () => navigate(location.href, false));
    setActive();
})();
