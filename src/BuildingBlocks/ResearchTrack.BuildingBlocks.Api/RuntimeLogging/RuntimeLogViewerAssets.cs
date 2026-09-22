namespace ResearchTrack.BuildingBlocks.Api.RuntimeLogging;

internal static class RuntimeLogViewerAssets
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>ResearchTrack Runtime Logs</title>
          <link rel="stylesheet" href="/_runtime-logs/assets/app.css">
        </head>
        <body>
          <header>
            <div>
              <p class="eyebrow">RESEARCHTRACK BACKEND</p>
              <h1>Runtime logs</h1>
            </div>
            <div class="connection"><span id="connection-dot"></span><span id="connection-label">Connecting</span></div>
          </header>
          <main>
            <section class="summary" aria-label="Log summary">
              <div><strong id="total-count">0</strong><span>Visible</span></div>
              <div><strong id="error-count">0</strong><span>Errors</span></div>
              <div><strong id="warning-count">0</strong><span>Warnings</span></div>
              <div><strong id="slow-count">0</strong><span>Slow requests</span></div>
            </section>
            <section class="toolbar" aria-label="Log controls">
              <label class="search"><span>Search</span><input id="search" type="search" placeholder="Message, path, trace ID..." autocomplete="off"></label>
              <label><span>Level</span><select id="level"><option value="">All levels</option><option>Error</option><option>Critical</option><option>Warning</option><option>Information</option><option>Debug</option><option>Trace</option></select></label>
              <label><span>Service</span><select id="service"><option value="">All services</option></select></label>
              <button id="pause" type="button">Pause</button>
              <button id="clear" class="danger" type="button">Clear</button>
            </section>
            <section class="log-shell">
              <div class="table-head"><span>Time</span><span>Level</span><span>Event</span><span>Duration</span><span>Trace ID</span></div>
              <div id="logs" class="logs" aria-live="polite"></div>
              <div id="empty" class="empty">Waiting for backend activity...</div>
            </section>
          </main>
          <template id="row-template">
            <article class="log-row">
              <button class="row-summary" type="button" aria-expanded="false">
                <time></time><span class="level"></span><span class="message"></span><span class="duration"></span><code class="trace"></code>
              </button>
              <div class="details" hidden><dl></dl><pre class="exception" hidden></pre></div>
            </article>
          </template>
          <script src="/_runtime-logs/assets/app.js" defer></script>
        </body>
        </html>
        """;

    public const string Css = """
        :root { color-scheme: light; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; background: #f5f6f4; color: #202522; }
        * { box-sizing: border-box; }
        body { margin: 0; min-width: 320px; }
        header { min-height: 92px; display: flex; align-items: center; justify-content: space-between; gap: 24px; padding: 18px clamp(18px, 4vw, 52px); background: #173f35; color: #fff; border-bottom: 4px solid #efb83d; }
        h1 { margin: 2px 0 0; font-size: 27px; line-height: 1.1; letter-spacing: 0; }
        .eyebrow { margin: 0; color: #b9d4cb; font-size: 11px; font-weight: 700; letter-spacing: 0; }
        .connection { display: flex; align-items: center; gap: 8px; font-size: 13px; }
        #connection-dot { width: 9px; height: 9px; border-radius: 50%; background: #efb83d; box-shadow: 0 0 0 4px rgb(239 184 61 / 18%); }
        .connection.live #connection-dot { background: #62d69a; box-shadow: 0 0 0 4px rgb(98 214 154 / 18%); }
        .connection.offline #connection-dot { background: #ef725f; box-shadow: 0 0 0 4px rgb(239 114 95 / 18%); }
        main { max-width: 1600px; margin: 0 auto; padding: 24px clamp(14px, 3vw, 38px) 40px; }
        .summary { display: grid; grid-template-columns: repeat(4, minmax(110px, 1fr)); border: 1px solid #d9ded9; background: #fff; }
        .summary div { display: flex; align-items: baseline; gap: 9px; padding: 14px 18px; border-right: 1px solid #e3e7e3; }
        .summary div:last-child { border-right: 0; }
        .summary strong { font-size: 22px; font-variant-numeric: tabular-nums; }
        .summary span { color: #68716b; font-size: 12px; }
        .toolbar { display: grid; grid-template-columns: minmax(240px, 1fr) 150px 210px auto auto; gap: 10px; align-items: end; padding: 18px 0 14px; }
        label { display: grid; gap: 5px; color: #59635c; font-size: 11px; font-weight: 700; }
        input, select, button { min-height: 38px; border: 1px solid #cbd2cc; border-radius: 5px; background: #fff; color: #252b27; font: inherit; letter-spacing: 0; }
        input, select { width: 100%; padding: 8px 10px; }
        input:focus, select:focus, button:focus-visible { outline: 3px solid rgb(31 120 92 / 22%); border-color: #1f785c; }
        button { padding: 8px 14px; cursor: pointer; font-weight: 700; }
        button:hover { background: #edf3ef; }
        button.danger { color: #9b2c23; border-color: #e3bbb7; }
        button.danger:hover { background: #fff0ee; }
        .log-shell { min-height: 420px; border: 1px solid #cfd6d0; background: #fff; overflow: hidden; }
        .table-head, .row-summary { display: grid; grid-template-columns: 92px 88px minmax(300px, 1fr) 92px minmax(125px, .35fr); gap: 12px; align-items: center; }
        .table-head { min-height: 36px; padding: 0 14px; background: #eef1ee; border-bottom: 1px solid #d7ddd8; color: #5e6861; font-size: 11px; font-weight: 700; }
        .log-row { border-bottom: 1px solid #ecefec; }
        .row-summary { width: 100%; min-height: 43px; padding: 7px 14px; border: 0; border-radius: 0; text-align: left; font-weight: 400; }
        .row-summary:hover { background: #f6f9f7; }
        .log-row.error .row-summary { box-shadow: inset 3px 0 #cb4638; }
        .log-row.warning .row-summary { box-shadow: inset 3px 0 #d29618; }
        .log-row.slow .duration { color: #a64c00; font-weight: 700; }
        time, .duration, .trace { color: #6e7771; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; font-variant-numeric: tabular-nums; }
        .level { justify-self: start; min-width: 72px; padding: 3px 7px; border-radius: 3px; background: #e8ece9; color: #4e5751; font-size: 10px; font-weight: 800; text-align: center; text-transform: uppercase; }
        .level.error, .level.critical { background: #ffe5e1; color: #a52d22; }
        .level.warning { background: #fff1cf; color: #815900; }
        .level.information { background: #dcf1e8; color: #176247; }
        .message { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }
        .trace { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .details { padding: 14px 18px 16px 194px; background: #fafbf9; border-top: 1px solid #edf0ed; }
        dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 6px 14px; margin: 0; font-size: 12px; }
        dt { color: #6b756e; font-weight: 700; }
        dd { margin: 0; overflow-wrap: anywhere; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
        pre { max-height: 280px; margin: 14px 0 0; padding: 12px; overflow: auto; background: #282d29; color: #f4f6f4; border-radius: 4px; font-size: 11px; white-space: pre-wrap; }
        .empty { padding: 70px 20px; color: #737d76; text-align: center; }
        @media (max-width: 900px) { .toolbar { grid-template-columns: 1fr 1fr; } .search, .toolbar label:nth-child(3) { grid-column: span 2; } .table-head { display: none; } .row-summary { grid-template-columns: 76px 78px 1fr 68px; } .trace { display: none; } .details { padding-left: 18px; } }
        @media (max-width: 560px) { header { min-height: 80px; } h1 { font-size: 22px; } .summary { grid-template-columns: 1fr 1fr; } .summary div:nth-child(2) { border-right: 0; } .summary div:nth-child(-n+2) { border-bottom: 1px solid #e3e7e3; } .toolbar { grid-template-columns: 1fr 1fr; } .row-summary { grid-template-columns: 64px 68px minmax(0, 1fr); gap: 8px; } .duration { display: none; } }
        """;

    public const string JavaScript = """
        (() => {
          const entries = new Map();
          const queued = [];
          let paused = false;
          const el = id => document.getElementById(id);
          const logs = el('logs');
          const empty = el('empty');
          const template = el('row-template');
          const connection = document.querySelector('.connection');

          const durationOf = entry => Number(entry.properties?.DurationMs ?? 0);
          const searchable = entry => JSON.stringify(entry).toLowerCase();
          const visibleEntries = () => [...entries.values()].filter(entry => {
            const query = el('search').value.trim().toLowerCase();
            return (!query || searchable(entry).includes(query)) &&
              (!el('level').value || entry.level === el('level').value) &&
              (!el('service').value || entry.service === el('service').value);
          });

          function render() {
            const visible = visibleEntries();
            const fragment = document.createDocumentFragment();
            for (const entry of visible) fragment.append(createRow(entry));
            logs.replaceChildren(fragment);
            empty.hidden = visible.length > 0;
            el('total-count').textContent = visible.length;
            el('error-count').textContent = visible.filter(x => x.level === 'Error' || x.level === 'Critical').length;
            el('warning-count').textContent = visible.filter(x => x.level === 'Warning').length;
            el('slow-count').textContent = visible.filter(x => durationOf(x) >= 1000).length;
            updateServices();
            if (!paused) logs.lastElementChild?.scrollIntoView({ block: 'nearest' });
          }

          function createRow(entry) {
            const row = template.content.firstElementChild.cloneNode(true);
            const level = entry.level.toLowerCase();
            row.classList.add(level);
            if (durationOf(entry) >= 1000) row.classList.add('slow');
            row.querySelector('time').textContent = new Date(entry.timestamp).toLocaleTimeString([], { hour12: false });
            const badge = row.querySelector('.level');
            badge.textContent = entry.level;
            badge.classList.add(level);
            row.querySelector('.message').textContent = entry.message;
            row.querySelector('.duration').textContent = entry.properties?.DurationMs == null ? '' : `${entry.properties.DurationMs} ms`;
            row.querySelector('.trace').textContent = entry.traceId || '';

            const details = row.querySelector('.details');
            const list = details.querySelector('dl');
            const fields = { Service: entry.service, Category: entry.category, 'Event ID': entry.eventId, 'Trace ID': entry.traceId, ...entry.properties };
            for (const [key, value] of Object.entries(fields)) {
              if (value == null || value === '') continue;
              const dt = document.createElement('dt');
              const dd = document.createElement('dd');
              dt.textContent = key;
              dd.textContent = typeof value === 'object' ? JSON.stringify(value) : String(value);
              list.append(dt, dd);
            }
            if (entry.exception) {
              const exception = details.querySelector('.exception');
              exception.textContent = entry.exception;
              exception.hidden = false;
            }
            const summary = row.querySelector('.row-summary');
            summary.addEventListener('click', () => {
              details.hidden = !details.hidden;
              summary.setAttribute('aria-expanded', String(!details.hidden));
            });
            return row;
          }

          function updateServices() {
            const select = el('service');
            const selected = select.value;
            const services = [...new Set([...entries.values()].map(x => x.service))].sort();
            select.replaceChildren(new Option('All services', ''), ...services.map(x => new Option(x, x)));
            select.value = selected;
          }

          function add(entry) {
            if (paused) { queued.push(entry); return; }
            entries.set(entry.id, entry);
            while (entries.size > 1000) entries.delete(entries.keys().next().value);
            render();
          }

          async function load() {
            const response = await fetch('/_runtime-logs/api/entries?limit=500', { cache: 'no-store' });
            if (!response.ok) throw new Error(`Log snapshot failed: ${response.status}`);
            for (const entry of await response.json()) entries.set(entry.id, entry);
            render();
          }

          function connect() {
            const source = new EventSource('/_runtime-logs/api/stream');
            source.onopen = () => { connection.className = 'connection live'; el('connection-label').textContent = 'Live'; };
            source.onmessage = event => add(JSON.parse(event.data));
            source.onerror = () => { connection.className = 'connection offline'; el('connection-label').textContent = 'Reconnecting'; };
          }

          el('search').addEventListener('input', render);
          el('level').addEventListener('change', render);
          el('service').addEventListener('change', render);
          el('pause').addEventListener('click', () => {
            paused = !paused;
            el('pause').textContent = paused ? 'Resume' : 'Pause';
            if (!paused) { for (const entry of queued.splice(0)) entries.set(entry.id, entry); render(); }
          });
          el('clear').addEventListener('click', async () => {
            const response = await fetch('/_runtime-logs/api/clear', { method: 'POST' });
            if (response.ok) { entries.clear(); queued.length = 0; render(); }
          });

          load().then(connect).catch(error => {
            connection.className = 'connection offline';
            el('connection-label').textContent = error.message;
          });
        })();
        """;
}
