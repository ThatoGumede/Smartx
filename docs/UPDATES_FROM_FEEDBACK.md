# Updates made based on feedback (Parts 1 and 2)

## A. Lecturer feedback and how it was addressed

I am completing this section from the feedback actually received on Parts 1 and 2. Replace the rows below with each
point of lecturer feedback, the change made, and where in the repository it can be seen.

| Part | Feedback received (lecturer's wording) | Change made | Where |
|---|---|---|---|
| 1 | *[paste feedback]* | *[describe change]* | *[file or commit]* |
| 2 | *[paste feedback]* | *[describe change]* | *[file or commit]* |

## B. Improvements I made between parts on my own initiative

These came from reviewing my own earlier work against the briefs, not from lecturer comments.

**Part 1 to Part 2**

1. The in-API mock generator was replaced by a separate device simulator that registers and reports over HTTP, because
   Part 2 requires simulated data to "hit the API like it was a device".
2. Duplicate MAC registrations are now rejected with 409 Conflict, and the device registry became a MAC-keyed
   `Dictionary` so telemetry finds its device in O(1).
3. Telemetry endpoints now return 202 Accepted and pass readings through a FIFO queue plus a priority lane for
   critical ones, instead of writing to the store inside the request.
4. Values rendered into the page by JavaScript are HTML-escaped, closing a script-injection gap in the Part 1 sensor
   table and dashboard.
5. The shared `AlertClassifier` replaced separate threshold logic, so a reading cannot be critical in one place and
   normal in another.
6. Page scripts were wrapped so they can be re-run safely, which lets the menu navigate without a full reload while
   keeping filters and state.

**Part 2 to final**

7. Float readings no longer leak binary precision into the API (`48.599998474121094` is now `48.6`).
8. The history store gained an AVL-indexed archive with delete support, so range queries do not scan a list and a
   per-sensor retention cap is possible.
9. The simulator now builds a connected mesh with redundant links and a deliberate single point of failure, so
   rerouting and isolation can both be demonstrated.
10. A dependency-free self-test project checks the new structures against independent oracles.
11. The README states what was and was not tested, including the commit count and that Docker and the Windows launcher
    were not run by me.
