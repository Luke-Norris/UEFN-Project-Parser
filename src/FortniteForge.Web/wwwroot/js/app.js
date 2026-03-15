// FortniteForge Web Dashboard
const $ = (sel, ctx = document) => ctx.querySelector(sel);
const $$ = (sel, ctx = document) => [...ctx.querySelectorAll(sel)];
const content = () => $('#content');

const api = async (path) => {
  const res = await fetch(`/api${path}`);
  if (!res.ok) throw new Error(await res.text());
  return res.json();
};

const apiPost = async (path) => {
  const res = await fetch(`/api${path}`, { method: 'POST' });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
};

const html = (strings, ...vals) => {
  let result = '';
  strings.forEach((str, i) => {
    result += str;
    if (i < vals.length) {
      const v = vals[i];
      result += v == null ? '' : typeof v === 'string' ? v : String(v);
    }
  });
  return result;
};

const esc = s => s ? s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') : '';
const fileSize = b => b < 1024 ? b + ' B' : b < 1048576 ? (b/1024).toFixed(1) + ' KB' : (b/1048576).toFixed(1) + ' MB';

// ========= Router =========
const routes = {
  '/': renderDashboard,
  '/levels': renderLevels,
  '/level': renderLevelDetail,
  '/assets': renderAssets,
  '/asset': renderAssetDetail,
  '/audit': renderAudit,
  '/staged': renderStaged,
};

function navigate() {
  const hash = location.hash.slice(1) || '/';
  const [path, ...paramParts] = hash.split('?');
  const params = new URLSearchParams(paramParts.join('?'));

  // Highlight active nav
  $$('.sidebar-nav a').forEach(a => {
    const page = a.dataset.page;
    a.classList.toggle('active', hash === '#/' + page || (page === 'dashboard' && hash === '/'));
  });

  const handler = routes[path] || routes['/'];
  content().innerHTML = '<div class="loading"><div class="spinner"></div> Loading...</div>';
  handler(params).catch(err => {
    content().innerHTML = `<div class="empty">Error: ${esc(err.message)}</div>`;
  });
}

window.addEventListener('hashchange', navigate);

// ========= Init =========
let statusData = null;

async function init() {
  try {
    statusData = await api('/status');
    $('#sidebar-project').textContent = statusData.projectName + (statusData.isUefnProject ? ' (UEFN)' : '');

    const modeColors = { ReadOnly: 'red', Staged: 'yellow', Direct: 'green' };
    const modeColor = modeColors[statusData.mode] || 'dim';
    const uefnColor = statusData.isUefnRunning ? 'green' : 'dim';
    const uefnLabel = statusData.isUefnRunning ? `Running (PID ${statusData.uefnPid})` : 'Not running';

    $('#sidebar-status').innerHTML = `
      <div><span class="indicator indicator-${uefnColor}"></span>UEFN: ${uefnLabel}</div>
      <div style="margin-top:4px"><span class="indicator indicator-${modeColor}"></span>Mode: ${statusData.mode}</div>
    `;
  } catch {
    $('#sidebar-project').textContent = 'Not connected';
  }
  navigate();
}

init();

// ========= Dashboard =========
async function renderDashboard() {
  const status = statusData || await api('/status');
  const modeColors = { ReadOnly: 'red', Staged: 'yellow', Direct: 'green' };

  let levelCount = 0;
  try {
    const levels = await api('/levels');
    levelCount = levels.length;
  } catch {}

  content().innerHTML = `
    <div class="page-header">
      <h2>Dashboard</h2>
      <div class="subtitle">${esc(status.projectName)} &mdash; ${status.isUefnProject ? 'UEFN Project' : 'Unreal Project'}</div>
    </div>

    <div class="card-grid">
      <div class="card">
        <div class="card-label">Assets</div>
        <div class="card-value accent">${status.assetCount.toLocaleString()}</div>
      </div>
      <div class="card">
        <div class="card-label">Levels</div>
        <div class="card-value purple">${levelCount}</div>
      </div>
      <div class="card">
        <div class="card-label">Verse Files</div>
        <div class="card-value green">${status.verseCount}</div>
      </div>
      <div class="card">
        <div class="card-label">Mode</div>
        <div class="card-value ${modeColors[status.mode] || ''}">${status.mode}</div>
      </div>
      <div class="card">
        <div class="card-label">UEFN</div>
        <div class="card-value ${status.isUefnRunning ? 'green' : ''}">${status.isUefnRunning ? 'Running' : 'Stopped'}</div>
      </div>
      <div class="card">
        <div class="card-label">URC</div>
        <div class="card-value ${status.urcActive ? 'yellow' : ''}">${status.hasUrc ? (status.urcActive ? 'Active' : 'Present') : 'None'}</div>
      </div>
      <div class="card">
        <div class="card-label">Staged Changes</div>
        <div class="card-value ${status.stagedFileCount > 0 ? 'yellow' : ''}">${status.stagedFileCount}</div>
      </div>
      <div class="card">
        <div class="card-label">Content Path</div>
        <div style="font-size:11px;color:var(--text-secondary);word-break:break-all;margin-top:4px">${esc(status.contentPath)}</div>
      </div>
    </div>

    <div class="page-header"><h2>Quick Access</h2></div>
    <div style="display:flex;gap:8px;flex-wrap:wrap">
      <a href="#/levels" class="btn">Browse Levels</a>
      <a href="#/assets" class="btn">Browse Assets</a>
      <a href="#/audit" class="btn">Run Audit</a>
      ${status.stagedFileCount > 0 ? '<a href="#/staged" class="btn btn-primary">View Staged Changes</a>' : ''}
    </div>
  `;
}

// ========= Levels =========
async function renderLevels() {
  const levels = await api('/levels');

  content().innerHTML = `
    <div class="page-header">
      <h2>Levels</h2>
      <div class="subtitle">${levels.length} level(s) found</div>
    </div>
    <div class="table-wrapper">
      <table>
        <thead><tr><th>Name</th><th>Path</th></tr></thead>
        <tbody>
          ${levels.map(l => `
            <tr>
              <td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a></td>
              <td style="color:var(--text-secondary);font-size:12px">${esc(l.relativePath)}</td>
            </tr>
          `).join('')}
        </tbody>
      </table>
    </div>
  `;
}

// ========= Level Detail =========
async function renderLevelDetail(params) {
  const path = params.get('path');
  if (!path) return content().innerHTML = '<div class="empty">No level path specified</div>';
  const name = path.split(/[\\/]/).pop().replace(/\.\w+$/, '');

  content().innerHTML = `
    <div class="page-header">
      <h2>${esc(name)}</h2>
      <div class="subtitle">${esc(path)}</div>
    </div>
    <div class="tabs">
      <button class="tab active" data-tab="actors">Actors</button>
      <button class="tab" data-tab="devices">Devices</button>
      <button class="tab" data-tab="spatial">Spatial Map</button>
      <button class="tab" data-tab="audit">Audit</button>
    </div>
    <div id="tab-content"><div class="loading"><div class="spinner"></div> Loading actors...</div></div>
  `;

  $$('.tab').forEach(t => t.addEventListener('click', () => {
    $$('.tab').forEach(x => x.classList.remove('active'));
    t.classList.add('active');
    loadTab(t.dataset.tab, path);
  }));

  loadTab('actors', path);
}

async function loadTab(tab, path) {
  const tc = $('#tab-content');
  tc.innerHTML = '<div class="loading"><div class="spinner"></div> Loading...</div>';

  try {
    switch (tab) {
      case 'actors': return await renderActorsTab(tc, path);
      case 'devices': return await renderDevicesTab(tc, path);
      case 'spatial': return await renderSpatialTab(tc, path);
      case 'audit': return await renderAuditTab(tc, path);
    }
  } catch (err) {
    tc.innerHTML = `<div class="empty">Error: ${esc(err.message)}</div>`;
  }
}

async function renderActorsTab(container, path) {
  const actors = await api(`/levels/actors?path=${encodeURIComponent(path)}`);

  // Group by class
  const groups = {};
  actors.forEach(a => {
    (groups[a.className] = groups[a.className] || []).push(a);
  });

  const sorted = Object.entries(groups).sort((a, b) => b[1].length - a[1].length);

  let treeHtml = '<div class="tree">';
  sorted.forEach(([cls, items]) => {
    treeHtml += `
      <div class="tree-node">
        <div class="tree-toggle" onclick="this.parentElement.classList.toggle('collapsed')">
          &#9662; ${esc(cls)} <span class="tree-count">(${items.length})</span>
        </div>
        <div class="tree-children">
          ${items.map(a => `<div class="tree-leaf">${esc(a.objectName)} <span class="tree-count">${a.propertyCount} props</span></div>`).join('')}
        </div>
      </div>
    `;
  });
  treeHtml += '</div>';

  container.innerHTML = `
    <div style="margin-bottom:12px;color:var(--text-secondary);font-size:13px">
      ${actors.length} exports across ${sorted.length} class types
    </div>
    ${treeHtml}
  `;
}

async function renderDevicesTab(container, path) {
  const devices = await api(`/levels/devices?path=${encodeURIComponent(path)}`);

  if (devices.length === 0) {
    container.innerHTML = '<div class="empty">No devices found in this level</div>';
    return;
  }

  container.innerHTML = `
    <div class="table-wrapper">
      <table>
        <thead><tr><th>Actor Name</th><th>Device Type</th><th>Label</th><th>Properties</th><th>Wiring</th></tr></thead>
        <tbody>
          ${devices.map(d => `
            <tr>
              <td>${esc(d.actorName)}</td>
              <td><span class="badge badge-blue">${esc(d.deviceType)}</span></td>
              <td>${esc(d.label || '')}</td>
              <td>${d.properties ? d.properties.length : 0}</td>
              <td>${d.wiring ? d.wiring.length : 0} connection(s)</td>
            </tr>
          `).join('')}
        </tbody>
      </table>
    </div>
  `;
}

async function renderSpatialTab(container, path) {
  container.innerHTML = '<div class="loading"><div class="spinner"></div> Scanning external actors (this may take a moment)...</div>';

  const data = await api(`/levels/spatial?path=${encodeURIComponent(path)}`);

  if (data.totalFiles === 0) {
    container.innerHTML = '<div class="empty">No external actors found for this level</div>';
    return;
  }

  // Summary cards
  let html = `
    <div class="card-grid" style="margin-bottom:24px">
      <div class="card"><div class="card-label">External Actors</div><div class="card-value accent">${data.totalFiles.toLocaleString()}</div></div>
      <div class="card"><div class="card-label">Parsed</div><div class="card-value green">${data.actors.length.toLocaleString()}</div></div>
      <div class="card"><div class="card-label">With Position</div><div class="card-value purple">${data.actorsWithPosition}</div></div>
      <div class="card"><div class="card-label">Class Types</div><div class="card-value">${data.classBreakdown.length}</div></div>
      ${data.parseErrors > 0 ? `<div class="card"><div class="card-label">Parse Errors</div><div class="card-value red">${data.parseErrors}</div></div>` : ''}
    </div>
  `;

  // Class breakdown chart
  const maxCount = data.classBreakdown[0]?.count || 1;
  const palette = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba','#79c0ff','#7ee787'];

  html += `<h3 style="margin-bottom:12px">Actor Class Breakdown</h3>`;
  html += `<div style="display:flex;flex-direction:column;gap:6px;margin-bottom:24px">`;
  data.classBreakdown.forEach((cls, i) => {
    const pct = (cls.count / maxCount * 100).toFixed(0);
    const color = i < palette.length ? palette[i] : '#484f58';
    html += `
      <div style="display:flex;align-items:center;gap:12px">
        <div style="width:250px;font-size:13px;text-align:right;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(cls.className)}">${esc(cls.className)}</div>
        <div style="flex:1;height:22px;background:var(--bg-tertiary);border-radius:4px;overflow:hidden">
          <div style="width:${pct}%;height:100%;background:${color};border-radius:4px;min-width:2px"></div>
        </div>
        <div style="width:50px;font-size:12px;color:var(--text-primary);font-weight:600">${cls.count}</div>
      </div>
    `;
  });
  html += '</div>';

  // If we have positions, render the spatial map
  const withPos = data.actors.filter(a => a.hasPosition);
  if (withPos.length > 0) {
    html += `
      <h3 style="margin-bottom:12px">Spatial Map (${withPos.length} positioned actors)</h3>
      <div class="spatial-container">
        <canvas id="spatial-canvas" width="900" height="600"></canvas>
        <div class="spatial-legend" id="spatial-legend"></div>
      </div>
      <div class="tooltip" id="spatial-tooltip" style="display:none"></div>
    `;
  }

  // Actor table (top 100)
  html += `<h3 style="margin:24px 0 12px">Actors (top 100 of ${data.actors.length})</h3>`;
  html += `<div class="table-wrapper"><table>
    <thead><tr><th>Name</th><th>Class</th><th>Position</th></tr></thead>
    <tbody>`;
  data.actors.slice(0, 100).forEach(a => {
    html += `<tr>
      <td style="font-size:12px">${esc(a.name)}</td>
      <td><span class="badge badge-blue">${esc(a.className)}</span></td>
      <td style="color:var(--text-muted);font-size:12px">${a.hasPosition ? `(${a.x.toFixed(0)}, ${a.y.toFixed(0)}, ${a.z.toFixed(0)})` : '<span class="badge badge-dim">N/A</span>'}</td>
    </tr>`;
  });
  html += '</tbody></table></div>';

  container.innerHTML = html;

  // Render spatial map if positions exist
  if (withPos.length > 0) renderSpatialCanvas(withPos);
}

function renderSpatialCanvas(actors) {
  const canvas = $('#spatial-canvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  const tooltip = $('#spatial-tooltip');
  const dpr = window.devicePixelRatio || 1;

  canvas.width = canvas.clientWidth * dpr;
  canvas.height = canvas.clientHeight * dpr;
  ctx.scale(dpr, dpr);

  const W = canvas.clientWidth, H = canvas.clientHeight;
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  actors.forEach(a => { if (a.x < minX) minX = a.x; if (a.x > maxX) maxX = a.x; if (a.y < minY) minY = a.y; if (a.y > maxY) maxY = a.y; });

  const rangeX = maxX - minX || 1, rangeY = maxY - minY || 1, pad = 40;
  const classColors = {};
  const palette = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba'];
  const classes = [...new Set(actors.map(a => a.className))];
  classes.forEach((c, i) => classColors[c] = i < palette.length ? palette[i] : '#484f58');

  let ox = 0, oy = 0, zoom = 1;
  const toScreen = (wx, wy) => [pad + ((wx-minX)/rangeX)*(W-pad*2), pad + ((wy-minY)/rangeY)*(H-pad*2)];

  function draw() {
    ctx.clearRect(0, 0, W, H);
    ctx.save(); ctx.translate(W/2, H/2); ctx.scale(zoom, zoom); ctx.translate(-W/2+ox, -H/2+oy);
    actors.forEach(a => { const [sx,sy] = toScreen(a.x, a.y); ctx.fillStyle = classColors[a.className]||'#484f58'; ctx.beginPath(); ctx.arc(sx, sy, 3, 0, Math.PI*2); ctx.fill(); });
    ctx.restore();
  }
  draw();

  canvas.addEventListener('wheel', e => { e.preventDefault(); zoom = Math.max(0.1, Math.min(20, zoom * (e.deltaY>0?0.9:1.1))); draw(); });
  let drag=false, lx, ly;
  canvas.addEventListener('mousedown', e => { drag=true; lx=e.clientX; ly=e.clientY; });
  canvas.addEventListener('mouseup', () => drag=false);
  canvas.addEventListener('mousemove', e => { if(drag){ox+=(e.clientX-lx)/zoom; oy+=(e.clientY-ly)/zoom; lx=e.clientX; ly=e.clientY; draw();} });
}

async function renderAuditTab(container, path) {
  const result = await api(`/audit/level?path=${encodeURIComponent(path)}`);
  renderAuditFindings(container, result);
}

function renderAuditFindings(container, result) {
  const severityIcons = {
    Error: { icon: '&#10060;', color: 'red' },
    Warning: { icon: '&#9888;', color: 'yellow' },
    Info: { icon: '&#8505;', color: 'accent' }
  };

  const statusBadge = {
    Pass: 'badge-green',
    Warning: 'badge-yellow',
    Fail: 'badge-red'
  };

  const findings = result.findings || [];

  container.innerHTML = `
    <div style="margin-bottom:16px;display:flex;align-items:center;gap:12px">
      <span class="badge ${statusBadge[result.status] || 'badge-dim'}">${result.status}</span>
      <span style="color:var(--text-secondary);font-size:13px">${findings.length} finding(s)</span>
    </div>
    ${findings.length === 0 ? '<div class="empty">No issues found</div>' :
      findings.map(f => {
        const sev = severityIcons[f.severity] || severityIcons.Info;
        return `
          <div class="finding">
            <div class="finding-icon" style="color:var(--${sev.color})">${sev.icon}</div>
            <div class="finding-body">
              <div class="finding-category">${esc(f.category)} &middot; ${esc(f.location || '')}</div>
              <div class="finding-message">${esc(f.message)}</div>
              ${f.suggestion ? `<div class="finding-suggestion">${esc(f.suggestion)}</div>` : ''}
            </div>
          </div>
        `;
      }).join('')
    }
  `;
}

// ========= Assets =========
let assetCache = null;

async function renderAssets() {
  content().innerHTML = `
    <div class="page-header">
      <h2>Assets</h2>
      <div class="subtitle">Browse and search project assets</div>
    </div>
    <div class="search-bar">
      <input type="text" id="asset-search" placeholder="Search assets...">
      <select id="asset-class-filter"><option value="">All Classes</option></select>
    </div>
    <div id="asset-table"><div class="loading"><div class="spinner"></div> Loading assets...</div></div>
  `;

  if (!assetCache) assetCache = await api('/assets');

  // Populate class filter
  const classes = [...new Set(assetCache.map(a => a.assetClass))].sort();
  const sel = $('#asset-class-filter');
  classes.forEach(c => { const o = document.createElement('option'); o.value = c; o.textContent = `${c} (${assetCache.filter(a=>a.assetClass===c).length})`; sel.appendChild(o); });

  function renderTable(filtered) {
    const shown = filtered.slice(0, 200);
    $('#asset-table').innerHTML = `
      <div style="margin-bottom:8px;font-size:12px;color:var(--text-muted)">
        Showing ${shown.length} of ${filtered.length} assets
      </div>
      <div class="table-wrapper">
        <table>
          <thead><tr><th>Name</th><th>Class</th><th>Size</th><th>Exports</th><th>Status</th></tr></thead>
          <tbody>
            ${shown.map(a => `
              <tr>
                <td><a href="#/asset?path=${encodeURIComponent(a.filePath)}">${esc(a.name)}</a>
                  <div style="font-size:11px;color:var(--text-muted)">${esc(a.relativePath)}</div></td>
                <td><span class="badge badge-blue">${esc(a.assetClass)}</span></td>
                <td>${fileSize(a.fileSize)}</td>
                <td>${a.exportCount}</td>
                <td>${a.isCooked ? '<span class="badge badge-red">Cooked</span>' :
                       a.isModifiable ? '<span class="badge badge-green">Modifiable</span>' :
                       '<span class="badge badge-dim">Read-only</span>'}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;
  }

  function filter() {
    const q = $('#asset-search').value.toLowerCase();
    const cls = $('#asset-class-filter').value;
    const filtered = assetCache.filter(a =>
      (!q || a.name.toLowerCase().includes(q) || a.relativePath.toLowerCase().includes(q) || a.assetClass.toLowerCase().includes(q)) &&
      (!cls || a.assetClass === cls)
    );
    renderTable(filtered);
  }

  $('#asset-search').addEventListener('input', filter);
  $('#asset-class-filter').addEventListener('change', filter);
  filter();
}

// ========= Asset Detail =========
async function renderAssetDetail(params) {
  const path = params.get('path');
  if (!path) return content().innerHTML = '<div class="empty">No asset path specified</div>';
  const name = path.split(/[\\/]/).pop().replace(/\.\w+$/, '');

  const detail = await api(`/assets/inspect?path=${encodeURIComponent(path)}`);

  content().innerHTML = `
    <div class="page-header">
      <h2>${esc(name)}</h2>
      <div class="subtitle">${esc(detail.relativePath)}</div>
    </div>

    <div class="card-grid" style="margin-bottom:24px">
      <div class="card">
        <div class="card-label">Class</div>
        <div style="font-size:14px;font-weight:600;color:var(--accent)">${esc(detail.assetClass)}</div>
      </div>
      <div class="card">
        <div class="card-label">Size</div>
        <div style="font-size:14px;font-weight:600">${fileSize(detail.fileSize)}</div>
      </div>
      <div class="card">
        <div class="card-label">Exports</div>
        <div style="font-size:14px;font-weight:600;color:var(--purple)">${detail.exportCount}</div>
      </div>
      <div class="card">
        <div class="card-label">Imports</div>
        <div style="font-size:14px;font-weight:600">${detail.importCount}</div>
      </div>
      <div class="card">
        <div class="card-label">Status</div>
        <div>${detail.isCooked ? '<span class="badge badge-red">Cooked</span>' :
               detail.isModifiable ? '<span class="badge badge-green">Modifiable</span>' :
               '<span class="badge badge-dim">Read-only</span>'}</div>
      </div>
    </div>

    <h3 style="margin-bottom:12px">Exports</h3>
    <div class="table-wrapper">
      <table>
        <thead><tr><th>#</th><th>Name</th><th>Class</th><th>Size</th><th>Properties</th></tr></thead>
        <tbody>
          ${(detail.exports || []).map(e => `
            <tr>
              <td>${e.index}</td>
              <td>${esc(e.objectName)}</td>
              <td><span class="badge badge-purple">${esc(e.className)}</span></td>
              <td>${fileSize(e.serialSize)}</td>
              <td>${e.properties ? e.properties.length : 0}</td>
            </tr>
            ${(e.properties && e.properties.length > 0) ? `
              <tr><td colspan="5" style="padding:0 16px 12px 48px;border:none">
                <div class="prop-list">
                  ${e.properties.map(p => `
                    <div class="prop-name">${esc(p.name)} <span class="prop-type">${esc(p.type)}</span></div>
                    <div class="prop-value">${esc(p.value)}</div>
                  `).join('')}
                </div>
              </td></tr>
            ` : ''}
          `).join('')}
        </tbody>
      </table>
    </div>

    ${(detail.imports && detail.imports.length > 0) ? `
      <h3 style="margin:24px 0 12px">Imports</h3>
      <div class="table-wrapper">
        <table>
          <thead><tr><th>#</th><th>Name</th><th>Class</th><th>Package</th></tr></thead>
          <tbody>
            ${detail.imports.map(i => `
              <tr>
                <td>${i.index}</td>
                <td>${esc(i.objectName)}</td>
                <td>${esc(i.className)}</td>
                <td style="color:var(--text-secondary)">${esc(i.packageName)}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    ` : ''}
  `;
}

// ========= Audit =========
async function renderAudit() {
  content().innerHTML = `
    <div class="page-header">
      <h2>Project Audit</h2>
      <div class="subtitle">Scanning for issues...</div>
    </div>
    <div class="loading"><div class="spinner"></div> Running audit...</div>
  `;

  const result = await api('/audit');
  content().innerHTML = `
    <div class="page-header">
      <h2>Project Audit</h2>
      <div class="subtitle">${esc(result.target)}</div>
    </div>
    <div id="audit-content"></div>
  `;

  renderAuditFindings($('#audit-content'), result);
}

// ========= Staged =========
async function renderStaged() {
  const staged = await api('/staged');

  content().innerHTML = `
    <div class="page-header">
      <h2>Staged Changes</h2>
      <div class="subtitle">${staged.length} file(s) staged for application</div>
    </div>

    ${staged.length === 0 ? '<div class="empty">No staged modifications. Changes made while UEFN is running are saved here instead of writing to your project directly.</div>' : `
      <div style="margin-bottom:16px;display:flex;gap:8px">
        <button class="btn btn-primary" id="apply-staged">Apply All</button>
        <button class="btn btn-danger" id="discard-staged">Discard All</button>
      </div>
      <div class="table-wrapper">
        <table>
          <thead><tr><th>File</th><th>Type</th><th>Size</th><th>Staged At</th></tr></thead>
          <tbody>
            ${staged.map(s => `
              <tr>
                <td>${esc(s.relativePath)}</td>
                <td><span class="badge ${s.exists ? 'badge-yellow' : 'badge-green'}">${s.exists ? 'Update' : 'New'}</span></td>
                <td>${fileSize(s.size)}</td>
                <td style="color:var(--text-secondary)">${new Date(s.stagedAt).toLocaleString()}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `}
  `;

  if (staged.length > 0) {
    $('#apply-staged')?.addEventListener('click', async () => {
      if (!confirm('Apply all staged changes to the project? UEFN must be closed.')) return;
      try {
        const results = await apiPost('/staged/apply');
        const ok = results.filter(r => r.success).length;
        const fail = results.filter(r => !r.success).length;
        alert(`Applied: ${ok}, Failed: ${fail}`);
        renderStaged();
      } catch (err) { alert('Error: ' + err.message); }
    });

    $('#discard-staged')?.addEventListener('click', async () => {
      if (!confirm('Discard all staged changes? This cannot be undone.')) return;
      try {
        await apiPost('/staged/discard');
        renderStaged();
      } catch (err) { alert('Error: ' + err.message); }
    });
  }
}
