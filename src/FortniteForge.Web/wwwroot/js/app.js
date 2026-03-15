// FortniteForge Web Dashboard
const $ = (sel, ctx = document) => ctx.querySelector(sel);
const $$ = (sel, ctx = document) => [...ctx.querySelectorAll(sel)];
const content = () => $('#content');
const api = async (path) => { const r = await fetch(`/api${path}`); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const apiPost = async (path, body) => { const r = await fetch(`/api${path}`, { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) }); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const esc = s => s ? String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') : '';
const fileSize = b => b < 1024 ? b+' B' : b < 1048576 ? (b/1024).toFixed(1)+' KB' : (b/1048576).toFixed(1)+' MB';
const truncate = (s, max) => s && s.length > max ? s.slice(0, max) + '...' : (s || '');

function toast(msg, type = 'info') {
  const el = document.createElement('div');
  el.className = `toast toast-${type}`;
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => el.classList.add('show'), 10);
  setTimeout(() => { el.classList.remove('show'); setTimeout(() => el.remove(), 300); }, 3000);
}

// ========= Router =========
const routes = { '/': renderDashboard, '/levels': renderLevels, '/level': renderLevelDetail, '/assets': renderAssets, '/asset': renderAssetDetail, '/audit': renderAudit, '/staged': renderStaged, '/projects': renderProjects, '/device': renderDeviceDetail };

let _currentRoute = null;
function navigate() {
  const hash = location.hash.slice(1) || '/';
  const [path, ...pp] = hash.split('?');
  const params = new URLSearchParams(pp.join('?'));

  // Don't re-render if already on this route with same params
  const routeKey = hash;
  if (routeKey === _currentRoute) return;
  _currentRoute = routeKey;

  $$('.sidebar-nav a').forEach(a => {
    const href = a.getAttribute('href');
    a.classList.toggle('active', href === '#' + path || (a.dataset.page === 'dashboard' && path === '/'));
  });

  const handler = routes[path] || routes['/'];
  content().innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  handler(params).catch(err => {
    content().innerHTML = `<div class="empty">${esc(err.message)}</div>`;
  });
}
window.addEventListener('hashchange', () => { _currentRoute = null; navigate(); });

// ========= State =========
let _status = null;
let _assetCache = null;
let _levelCache = {};

function clearCaches() { _assetCache = null; _levelCache = {}; _status = null; }

async function getStatus() {
  if (!_status) { try { _status = await api('/status'); } catch { _status = {}; } }
  return _status;
}

// ========= Init =========
async function init() {
  try {
    const s = await getStatus();
    const pData = await api('/projects');
    const active = pData.projects.find(p => p.id === pData.activeProjectId);

    if (active) {
      const typeLabel = active.type === 'Library' ? 'Library' : 'My Project';
      $('#sidebar-project').innerHTML = `${esc(active.name)} <span style="font-size:10px;color:var(--text-muted)">${typeLabel}</span>`;
    } else {
      $('#sidebar-project').innerHTML = '<a href="#/projects" style="color:var(--yellow);text-decoration:none">Add a project</a>';
    }

    const mc = { ReadOnly: 'red', Staged: 'yellow', Direct: 'green' };
    const mode = s.mode || 'None';
    $('#sidebar-status').innerHTML = `
      <div><span class="indicator indicator-${s.isUefnRunning ? 'green' : 'dim'}"></span>UEFN: ${s.isUefnRunning ? 'Running' : 'Off'}</div>
      <div style="margin-top:4px"><span class="indicator indicator-${mc[mode]||'dim'}"></span>${mode}</div>
    `;
  } catch {
    $('#sidebar-project').textContent = 'Not connected';
  }
  navigate();
}
init();

// ========= Projects =========
async function renderProjects() {
  const data = await api('/projects');
  const projects = data.projects;

  content().innerHTML = `
    <div class="page-header"><h2>Projects</h2></div>

    ${projects.length > 0 ? `<div class="table-wrapper" style="margin-bottom:32px"><table>
      <thead><tr><th>Project</th><th>Type</th><th>Defs</th><th>Actors</th><th>Verse</th><th></th></tr></thead><tbody>
        ${projects.map(p => `<tr class="${p.id === data.activeProjectId ? 'active-row' : ''}">
          <td><strong>${esc(p.name)}</strong>
            ${p.id === data.activeProjectId ? ' <span class="badge badge-green">Active</span>' : ''}
            ${p.isUefnProject ? ' <span class="badge badge-blue">UEFN</span>' : ''}
            <div style="font-size:10px;color:var(--text-muted);margin-top:2px">${esc(p.projectPath)}</div></td>
          <td><span class="badge ${p.type === 'Library' ? 'badge-purple' : 'badge-green'}">${p.type === 'Library' ? 'Library' : 'My Project'}</span></td>
          <td>${p.assetCount}</td><td>${p.externalActorCount.toLocaleString()}</td><td>${p.verseFileCount}</td>
          <td style="white-space:nowrap">
            ${p.id !== data.activeProjectId ? `<button class="btn" style="font-size:11px" onclick="activateProject('${p.id}')">Activate</button> ` : ''}
            <button class="btn btn-danger" style="font-size:11px" onclick="removeProject('${p.id}','${esc(p.name)}')">Remove</button>
          </td>
        </tr>`).join('')}
      </tbody></table></div>` : ''}

    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
      <div class="card">
        <h3 style="margin-bottom:8px;font-size:14px">Scan for Projects</h3>
        <div style="font-size:12px;color:var(--text-secondary);margin-bottom:8px">Find all UEFN projects in a folder</div>
        <div class="search-bar" style="margin-bottom:0">
          <input type="text" id="scan-path" placeholder="Paste folder path">
          <button class="btn" id="scan-browse">Browse</button>
          <button class="btn btn-primary" id="scan-btn">Scan</button>
        </div>
        <div id="scan-results" style="margin-top:8px"></div>
      </div>

      <div class="card">
        <h3 style="margin-bottom:8px;font-size:14px">Add Single Project</h3>
        <div style="font-size:12px;color:var(--text-secondary);margin-bottom:8px">Add a specific UEFN project folder</div>
        <div class="search-bar" style="margin-bottom:8px">
          <input type="text" id="add-path" placeholder="Paste project path">
          <button class="btn" id="add-browse">Browse</button>
        </div>
        <div style="display:flex;gap:12px;margin-bottom:8px">
          <label style="cursor:pointer;display:flex;align-items:center;gap:4px;font-size:12px"><input type="radio" name="ptype" value="MyProject" checked> My Project</label>
          <label style="cursor:pointer;display:flex;align-items:center;gap:4px;font-size:12px"><input type="radio" name="ptype" value="Library"> Library</label>
        </div>
        <button class="btn btn-primary" style="font-size:12px" id="add-btn">Add</button>
      </div>
    </div>
  `;

  // Browse buttons
  $('#scan-browse').addEventListener('click', () => pickFolder('scan-path'));
  $('#add-browse').addEventListener('click', () => pickFolder('add-path'));

  // Add
  $('#add-btn').addEventListener('click', async () => {
    const path = $('#add-path').value.trim();
    if (!path) return;
    try {
      await apiPost('/projects/add', { path, type: $('input[name="ptype"]:checked').value });
      clearCaches(); toast('Project added', 'success');
      _currentRoute = null; renderProjects();
    } catch (err) { toast(err.message, 'error'); }
  });

  // Scan
  $('#scan-btn').addEventListener('click', async () => {
    const path = $('#scan-path').value.trim();
    if (!path) return;
    $('#scan-results').innerHTML = '<div class="loading" style="padding:8px"><div class="spinner"></div></div>';
    try {
      const found = await api(`/projects/scan?path=${encodeURIComponent(path)}`);
      if (!found.length) { $('#scan-results').innerHTML = '<div style="color:var(--text-muted);font-size:12px;padding:8px">No projects found</div>'; return; }
      $('#scan-results').innerHTML = `<div style="max-height:300px;overflow-y:auto">${found.map(p =>
        `<div style="display:flex;justify-content:space-between;align-items:center;padding:6px 0;border-bottom:1px solid var(--border);font-size:12px">
          <div><strong>${esc(p.projectName)}</strong> <span style="color:var(--text-muted)">${p.assetCount}d / ${p.externalActorCount}a / ${p.verseFileCount}v</span></div>
          ${p.alreadyAdded ? '<span class="badge badge-dim">Added</span>' : `<button class="btn" style="font-size:10px;padding:2px 8px" onclick="addScanned('${esc(p.projectPath).replace(/\\/g,'\\\\').replace(/'/g,"\\'")}','${esc(p.projectName).replace(/'/g,"\\'")}')">Add</button>`}
        </div>`
      ).join('')}</div>`;
    } catch (err) { $('#scan-results').innerHTML = `<div style="color:var(--red);font-size:12px">${esc(err.message)}</div>`; }
  });
}

async function pickFolder(inputId) {
  try { const r = await api('/browse/pick-folder'); if (!r.cancelled && r.path) $('#' + inputId).value = r.path; }
  catch (err) { toast('Could not open folder picker', 'error'); }
}

window.activateProject = async (id) => { await apiPost('/projects/activate', { id }); clearCaches(); _currentRoute = null; await init(); toast('Project activated', 'success'); };
window.removeProject = async (id, name) => { if (!confirm(`Remove "${name}"?`)) return; await apiPost('/projects/remove', { id }); clearCaches(); _currentRoute = null; renderProjects(); };
window.addScanned = async (path, name) => { try { await apiPost('/projects/add', { path, type: 'Library', name }); clearCaches(); toast(`Added ${name}`, 'success'); _currentRoute = null; renderProjects(); } catch(e) { toast(e.message, 'error'); } };

// ========= Dashboard =========
async function renderDashboard() {
  const s = await getStatus();
  if (!s.projectName || s.projectName === 'No Project') {
    content().innerHTML = `<div class="empty" style="padding:80px 20px"><h2 style="margin-bottom:12px">Welcome to FortniteForge</h2>
      <p style="color:var(--text-secondary);margin-bottom:20px">Add a UEFN project to get started.</p>
      <a href="#/projects" class="btn btn-primary">Add Project</a></div>`;
    return;
  }

  let levels = []; try { levels = await api('/levels'); } catch {}

  content().innerHTML = `
    <div class="page-header"><h2>${esc(s.projectName)}</h2>
      <div class="subtitle">${esc(s.projectPath || '')}</div></div>
    <div class="card-grid">
      <div class="card"><div class="card-label">Definitions</div><div class="card-value accent">${s.definitionCount || 0}</div></div>
      <div class="card"><div class="card-label">Placed Actors</div><div class="card-value purple">${(s.assetCount - (s.definitionCount||0)).toLocaleString()}</div></div>
      <div class="card"><div class="card-label">Levels</div><div class="card-value">${levels.length}</div></div>
      <div class="card"><div class="card-label">Verse</div><div class="card-value green">${s.verseCount || 0}</div></div>
      <div class="card"><div class="card-label">Mode</div><div class="card-value">${s.mode || 'N/A'}</div></div>
    </div>
    ${levels.length > 0 ? `<h3 style="margin:24px 0 12px">Levels</h3><div class="table-wrapper"><table><thead><tr><th>Level</th><th></th></tr></thead><tbody>
      ${levels.map(l => `<tr>
        <td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a></td>
        <td style="text-align:right"><a href="#/level?path=${encodeURIComponent(l.filePath)}&tab=devices" class="btn" style="font-size:11px">View Devices</a></td>
      </tr>`).join('')}</tbody></table></div>` : ''}
  `;
}

// ========= Levels =========
async function renderLevels() {
  const pData = await api('/projects');
  let allLevels = [];
  for (const p of pData.projects) {
    try { const lvls = await api(`/levels?projectId=${p.id}`); lvls.forEach(l => { l._project = p.name; l._type = p.type; }); allLevels.push(...lvls); } catch {}
  }
  content().innerHTML = `
    <div class="page-header"><h2>All Levels</h2><div class="subtitle">${allLevels.length} level(s) across ${pData.projects.length} project(s)</div></div>
    <div class="table-wrapper"><table><thead><tr><th>Level</th><th>Project</th><th></th></tr></thead><tbody>
      ${allLevels.map(l => `<tr><td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a></td>
        <td><span class="badge ${l._type==='Library'?'badge-purple':'badge-green'}">${esc(l._project)}</span></td>
        <td style="text-align:right"><a href="#/level?path=${encodeURIComponent(l.filePath)}&tab=devices" class="btn" style="font-size:11px">Devices</a></td></tr>`).join('')}
    </tbody></table></div>`;
}

// ========= Level Detail =========
async function renderLevelDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const name = path.split(/[\\/]/).pop().replace(/\.\w+$/, '');
  const tab = params.get('tab') || 'devices';

  content().innerHTML = `
    <div class="page-header"><h2>${esc(name)}</h2></div>
    <div class="tabs">
      <button class="tab ${tab==='devices'?'active':''}" data-tab="devices">Devices</button>
      <button class="tab ${tab==='actors'?'active':''}" data-tab="actors">Static Actors</button>
      <button class="tab ${tab==='audit'?'active':''}" data-tab="audit">Audit</button>
    </div>
    <div id="tab-content"></div>`;

  const loadTab = async (t) => {
    const tc = $('#tab-content');
    tc.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
    try {
      if (t === 'devices') await renderDevicesTab(tc, path);
      else if (t === 'actors') await renderActorsTab(tc, path);
      else if (t === 'audit') { const r = await api(`/audit/level?path=${encodeURIComponent(path)}`); renderAuditFindings(tc, r); }
    } catch (err) { tc.innerHTML = `<div class="empty">${esc(err.message)}</div>`; }
  };

  $$('.tab').forEach(t => t.addEventListener('click', () => {
    $$('.tab').forEach(x => x.classList.remove('active'));
    t.classList.add('active');
    loadTab(t.dataset.tab);
  }));
  loadTab(tab);
}

async function renderDevicesTab(c, path) {
  if (!_levelCache[path]) _levelCache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
  const d = _levelCache[path];

  if (d.deviceCount === 0) { c.innerHTML = `<div class="empty">No devices found. ${d.staticActorCount} static actors in this level.</div>`; return; }

  const groups = {};
  d.devices.forEach(dv => (groups[dv.className] = groups[dv.className] || []).push(dv));
  const sorted = Object.entries(groups).sort((a,b) => b[1].length - a[1].length);

  let h = `
    <div class="card-grid" style="margin-bottom:16px">
      <div class="card"><div class="card-label">Devices</div><div class="card-value accent">${d.deviceCount}</div></div>
      <div class="card"><div class="card-label">Types</div><div class="card-value purple">${sorted.length}</div></div>
      <div class="card"><div class="card-label">Static Actors</div><div class="card-value">${d.staticActorCount}</div></div>
    </div>
    <div class="search-bar"><input type="text" id="dev-search" placeholder="Search devices..."></div><div id="dev-list">`;

  sorted.forEach(([cls, devs]) => {
    h += `<div class="device-group"><div class="device-group-header" onclick="this.parentElement.classList.toggle('collapsed')">
      <span class="tree-toggle">&#9662;</span><span class="badge badge-blue">${esc(devs[0].displayName)}</span>
      <span class="tree-count">${devs.length}</span></div><div class="device-group-items">`;
    devs.forEach((dv, i) => {
      const ed = dv.properties.filter(p => p.isEditable);
      h += `<div class="device-card" data-search="${esc((dv.displayName+' '+dv.className).toLowerCase())}">
        <div class="device-card-header"><div><strong>${esc(dv.displayName)}${devs.length>1?' #'+(i+1):''}</strong>
          <span style="color:var(--text-muted);font-size:11px;margin-left:6px">${dv.totalPropertyCount} props</span></div>
          <a href="#/device?path=${encodeURIComponent(dv.filePath)}" class="btn" style="font-size:11px;padding:3px 10px">Inspect</a></div>
        ${dv.hasPosition ? `<div style="font-size:11px;color:var(--text-secondary);margin-top:4px">(${dv.x.toFixed(0)}, ${dv.y.toFixed(0)}, ${dv.z.toFixed(0)})</div>` : ''}
        ${ed.length > 0 ? `<div class="device-props-preview">${ed.slice(0,3).map(p =>
          `<div class="prop-row"><span class="prop-name">${esc(p.name)}</span><span class="prop-value editable">${esc(truncate(p.value,40))}</span></div>`
        ).join('')}${ed.length>3?`<div style="font-size:10px;color:var(--text-muted)">+${ed.length-3} more</div>`:''}</div>` : ''}
      </div>`;
    });
    h += '</div></div>';
  });
  h += '</div>';
  c.innerHTML = h;

  $('#dev-search')?.addEventListener('input', e => {
    const q = e.target.value.toLowerCase();
    $$('.device-card').forEach(card => card.style.display = !q || card.dataset.search.includes(q) ? '' : 'none');
    $$('.device-group').forEach(g => g.style.display = $$('.device-card',g).some(c => c.style.display !== 'none') ? '' : 'none');
  });
}

async function renderActorsTab(c, path) {
  if (!_levelCache[path]) _levelCache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
  const d = _levelCache[path];
  const bd = d.staticActorBreakdown || [];
  const max = bd[0]?.count || 1;
  const pal = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba','#79c0ff','#7ee787'];
  c.innerHTML = `
    <div class="card-grid" style="margin-bottom:16px">
      <div class="card"><div class="card-label">Static Actors</div><div class="card-value">${d.staticActorCount}</div></div>
      <div class="card"><div class="card-label">Class Types</div><div class="card-value purple">${bd.length}</div></div>
    </div>
    <div style="display:flex;flex-direction:column;gap:3px">${bd.map((cls,i) =>
      `<div style="display:flex;align-items:center;gap:10px"><div style="width:200px;font-size:11px;text-align:right;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(cls.displayName)}</div>
      <div style="flex:1;height:16px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden"><div style="width:${(cls.count/max*100).toFixed(0)}%;height:100%;background:${pal[i%pal.length]};border-radius:3px;min-width:2px"></div></div>
      <div style="width:36px;font-size:11px;font-weight:600;text-align:right">${cls.count}</div></div>`
    ).join('')}</div>`;
}

// ========= Device Detail =========
async function renderDeviceDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const dv = await api(`/device/inspect?path=${encodeURIComponent(path)}`);
  const ed = dv.properties.filter(p => p.isEditable);
  const other = dv.properties.filter(p => !p.isEditable);

  content().innerHTML = `
    <div class="page-header"><h2>${esc(dv.displayName)}</h2><div class="subtitle">${esc(dv.className)}</div></div>
    <div class="card-grid" style="margin-bottom:20px">
      <div class="card"><div class="card-label">Class</div><div style="font-size:12px;color:var(--accent)">${esc(dv.className)}</div></div>
      ${dv.hasPosition ? `<div class="card"><div class="card-label">Position</div><div style="font-size:12px">(${dv.x.toFixed(0)}, ${dv.y.toFixed(0)}, ${dv.z.toFixed(0)})</div></div>` : ''}
      <div class="card"><div class="card-label">Components</div><div class="card-value purple">${dv.components.length}</div></div>
      <div class="card"><div class="card-label">Editable</div><div class="card-value green">${ed.length}</div></div>
    </div>

    <div id="pending-banner"></div>

    ${ed.length > 0 ? `<h3 style="margin-bottom:8px">Configurable Properties</h3>
    <div class="table-wrapper"><table><thead><tr><th>Property</th><th>Value</th><th style="width:80px">Type</th><th style="width:20px"></th></tr></thead><tbody>
      ${ed.map(p => {
        const inputType = p.type === 'BoolProperty' ? 'checkbox' : p.type === 'IntProperty' ? 'number' : 'text';
        return `<tr data-prop="${esc(p.name)}" data-orig="${esc(p.value)}" data-type="${esc(p.type)}">
          <td><span class="prop-name">${esc(p.name)}</span></td>
          <td>${inputType === 'checkbox'
            ? `<label class="toggle"><input type="checkbox" class="prop-edit" ${p.value==='True'?'checked':''} data-prop="${esc(p.name)}"><span class="toggle-label">${p.value}</span></label>`
            : `<input type="${inputType}" class="prop-edit prop-input" value="${esc(p.value)}" data-prop="${esc(p.name)}">`}</td>
          <td><span class="badge badge-dim">${esc(p.type.replace('Property',''))}</span></td>
          <td class="change-indicator"></td></tr>`;
      }).join('')}
    </tbody></table></div>` : ''}

    ${other.length > 0 ? `<details style="margin-top:20px"><summary style="cursor:pointer;color:var(--text-secondary);font-size:13px">Other Properties (${other.length})</summary>
      <div class="table-wrapper" style="margin-top:8px"><table><thead><tr><th>Property</th><th>Value</th><th>Type</th></tr></thead><tbody>
        ${other.map(p => `<tr><td style="color:var(--text-secondary);font-size:12px">${esc(p.name)}</td><td style="color:var(--text-muted);font-size:11px">${esc(truncate(p.value,100))}</td><td><span class="badge badge-dim" style="font-size:10px">${esc(p.type.replace('Property',''))}</span></td></tr>`).join('')}
      </tbody></table></div></details>` : ''}

    <details style="margin-top:16px"><summary style="cursor:pointer;color:var(--text-secondary);font-size:13px">Components (${dv.components.length})</summary>
      <div class="table-wrapper" style="margin-top:8px"><table><thead><tr><th>Name</th><th>Class</th><th>Props</th></tr></thead><tbody>
        ${dv.components.map(c => `<tr><td style="font-size:12px">${esc(c.objectName)}</td><td><span class="badge badge-purple" style="font-size:10px">${esc(c.className)}</span></td><td>${c.propertyCount}</td></tr>`).join('')}
      </tbody></table></div></details>
    <div style="margin-top:20px;font-size:10px;color:var(--text-muted)">${esc(path)}</div>`;

  // Wire editing
  $$('.prop-edit').forEach(input => {
    input.addEventListener('change', async () => {
      const row = input.closest('tr');
      const propName = input.dataset.prop;
      const origVal = row.dataset.orig;
      const newVal = input.type === 'checkbox' ? (input.checked ? 'True' : 'False') : input.value;
      const indicator = row.querySelector('.change-indicator');
      const comp = dv.components.find(c => c.properties.some(p => p.name === propName));

      if (newVal !== origVal) {
        indicator.innerHTML = '<span style="color:var(--yellow)">&#9679;</span>';
        input.style.borderColor = 'var(--yellow)';
        await apiPost('/changes/add', { filePath: path, exportName: comp?.objectName || '', propertyName: propName, oldValue: origVal, newValue: newVal, propertyType: row.dataset.type, deviceName: dv.displayName });
      } else {
        indicator.innerHTML = '';
        input.style.borderColor = '';
        await apiPost('/changes/remove', { filePath: path, exportName: '', propertyName: propName });
      }
      refreshPendingBanner();
    });
  });
  refreshPendingBanner();
}

async function refreshPendingBanner() {
  const banner = $('#pending-banner');
  if (!banner) return;
  let changes; try { changes = await api('/changes'); } catch { return; }
  if (!changes.length) { banner.innerHTML = ''; return; }

  banner.innerHTML = `<div class="pending-card">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
      <strong>${changes.length} pending change(s)</strong>
      <div><button class="btn btn-primary" style="font-size:11px" onclick="applyChanges()">Apply</button>
        <button class="btn" style="font-size:11px;margin-left:4px" onclick="discardChanges()">Discard</button></div>
    </div>
    ${changes.map(c => `<div class="diff-line">
      <span style="color:var(--text-muted);min-width:100px;font-size:11px">${esc(c.deviceName)}</span>
      <span class="prop-name" style="min-width:120px">${esc(c.propertyName)}</span>
      <span class="diff-old">${esc(truncate(c.oldValue,25))}</span>
      <span style="color:var(--text-muted)">&rarr;</span>
      <span class="diff-new">${esc(truncate(c.newValue,25))}</span>
    </div>`).join('')}
  </div>`;
}

window.applyChanges = async () => {
  if (!confirm('Apply all pending changes?')) return;
  try {
    const r = await apiPost('/changes/apply');
    toast(`Applied ${r.applied}/${r.total} changes`, r.applied === r.total ? 'success' : 'warning');
    _levelCache = {};
    refreshPendingBanner();
  } catch (err) { toast(err.message, 'error'); }
};
window.discardChanges = async () => { await apiPost('/changes/clear'); refreshPendingBanner(); toast('Changes discarded'); };

// ========= Assets =========
async function renderAssets() {
  content().innerHTML = `<div class="page-header"><h2>Asset Definitions</h2><div class="subtitle">Blueprints, materials, custom props (excludes placed instances)</div></div>
    <div class="search-bar"><input type="text" id="asset-search" placeholder="Search..."></div>
    <div id="asset-list"><div class="loading"><div class="spinner"></div></div></div>`;

  if (!_assetCache) _assetCache = await api('/assets');

  // Build class filter options
  const classes = [...new Set(_assetCache.map(a => a.assetClass))].sort();
  const filterHtml = `<select id="asset-class-filter" style="background:var(--bg-tertiary);border:1px solid var(--border);border-radius:4px;padding:4px 8px;color:var(--text-primary);font-size:12px">
    <option value="">All Types (${_assetCache.length})</option>
    ${classes.map(c => `<option value="${esc(c)}">${esc(c)} (${_assetCache.filter(a=>a.assetClass===c).length})</option>`).join('')}
  </select>`;
  $('.search-bar').insertAdjacentHTML('beforeend', filterHtml);

  const render = (f) => {
    if (!f.length) { $('#asset-list').innerHTML = '<div class="empty">No definitions found</div>'; return; }

    // Group by class
    const groups = {};
    f.forEach(a => (groups[a.assetClass] = groups[a.assetClass] || []).push(a));
    const sorted = Object.entries(groups).sort((a,b) => b[1].length - a[1].length);

    let html = `<div style="margin-bottom:6px;font-size:11px;color:var(--text-muted)">${f.length} definition(s) across ${sorted.length} type(s)</div>`;
    sorted.forEach(([cls, assets]) => {
      html += `<div class="device-group" style="margin-bottom:6px">
        <div class="device-group-header" onclick="this.parentElement.classList.toggle('collapsed')">
          <span class="tree-toggle">&#9662;</span>
          <span class="badge badge-purple">${esc(cls)}</span>
          <span class="tree-count">${assets.length}</span>
        </div>
        <div class="device-group-items">
          <div class="table-wrapper" style="margin:0"><table><tbody>
            ${assets.slice(0,50).map(a => `<tr>
              <td><a href="#/asset?path=${encodeURIComponent(a.filePath)}">${esc(a.name)}</a></td>
              <td style="font-size:11px;color:var(--text-muted);width:80px">${fileSize(a.fileSize)}</td>
            </tr>`).join('')}
            ${assets.length > 50 ? `<tr><td colspan="2" style="color:var(--text-muted);font-size:11px">+${assets.length - 50} more</td></tr>` : ''}
          </tbody></table></div>
        </div>
      </div>`;
    });
    $('#asset-list').innerHTML = html;
  };

  const filter = () => {
    const q = $('#asset-search').value.toLowerCase();
    const cls = $('#asset-class-filter')?.value || '';
    render(_assetCache.filter(a => (!q || a.name.toLowerCase().includes(q) || (a.relativePath||'').toLowerCase().includes(q)) && (!cls || a.assetClass === cls)));
  };
  $('#asset-search').addEventListener('input', filter);
  $('#asset-class-filter')?.addEventListener('change', filter);
  filter();
}

async function renderAssetDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const d = await api(`/assets/inspect?path=${encodeURIComponent(path)}`);
  content().innerHTML = `
    <div class="page-header"><h2>${esc(d.name)}</h2><div class="subtitle">${esc(d.relativePath)}</div></div>
    <div class="card-grid" style="margin-bottom:20px">
      <div class="card"><div class="card-label">Class</div><div style="font-size:12px;color:var(--accent)">${esc(d.assetClass)}</div></div>
      <div class="card"><div class="card-label">Size</div><div style="font-size:12px">${fileSize(d.fileSize)}</div></div>
      <div class="card"><div class="card-label">Exports</div><div class="card-value purple">${d.exportCount}</div></div>
      <div class="card"><div class="card-label">Imports</div><div class="card-value">${d.importCount}</div></div>
    </div>
    ${(d.exports||[]).map(e => `<details class="card" style="margin-bottom:6px">
      <summary style="cursor:pointer;display:flex;justify-content:space-between;align-items:center">
        <span><span class="badge badge-purple" style="font-size:10px">${esc(e.className)}</span> <span style="font-size:12px">${esc(e.objectName)}</span></span>
        <span style="font-size:10px;color:var(--text-muted)">${e.properties?.length||0} props</span></summary>
      ${(e.properties?.length > 0) ? `<div class="prop-list" style="margin-top:8px;padding-top:8px;border-top:1px solid var(--border)">
        ${e.properties.map(p => `<div class="prop-name">${esc(p.name)} <span class="prop-type">${esc(p.type)}</span></div><div class="prop-value">${esc(truncate(p.value,100))}</div>`).join('')}
      </div>` : '<div style="margin-top:8px;font-size:11px;color:var(--text-muted)">No properties</div>'}
    </details>`).join('')}`;
}

// ========= Audit =========
async function renderAudit() {
  content().innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  try {
    const r = await api('/audit');
    content().innerHTML = `<div class="page-header"><h2>Project Audit</h2></div><div id="ac"></div>`;
    renderAuditFindings($('#ac'), r);
  } catch (err) { content().innerHTML = `<div class="empty">${esc(err.message)}</div>`; }
}

function renderAuditFindings(c, r) {
  const ic = { Error: {i:'&#10060;',c:'red'}, Warning: {i:'&#9888;&#65039;',c:'yellow'}, Info: {i:'&#8505;&#65039;',c:'accent'} };
  const f = r.findings || [];
  c.innerHTML = `<div style="margin-bottom:12px"><span class="badge ${({Pass:'badge-green',Warning:'badge-yellow',Fail:'badge-red'})[r.status]||'badge-dim'}">${r.status}</span>
    <span style="color:var(--text-secondary);font-size:13px;margin-left:8px">${f.length} finding(s)</span></div>
    ${!f.length ? '<div style="color:var(--text-muted)">No issues found.</div>' : f.map(fi => {
      const s = ic[fi.severity]||ic.Info;
      return `<div class="finding"><div class="finding-icon" style="color:var(--${s.c})">${s.i}</div><div class="finding-body">
        <div style="font-size:11px;color:var(--text-muted)">${esc(fi.category)}</div>
        <div style="font-size:13px">${esc(fi.message)}</div>
        ${fi.suggestion?`<div style="font-size:12px;color:var(--text-secondary);margin-top:2px">${esc(fi.suggestion)}</div>`:''}</div></div>`;
    }).join('')}`;
}

// ========= Staged =========
async function renderStaged() {
  const s = await api('/staged');
  content().innerHTML = `<div class="page-header"><h2>Staged Changes</h2></div>
    ${!s.length ? '<div class="empty">No staged modifications.</div>' : `
      <div style="margin-bottom:12px;display:flex;gap:8px"><button class="btn btn-primary" onclick="applyStaged()">Apply All</button><button class="btn btn-danger" onclick="discardStaged()">Discard All</button></div>
      <div class="table-wrapper"><table><thead><tr><th>File</th><th>Type</th><th>Size</th></tr></thead><tbody>
        ${s.map(f => `<tr><td style="font-size:12px">${esc(f.relativePath)}</td><td><span class="badge ${f.exists?'badge-yellow':'badge-green'}">${f.exists?'Update':'New'}</span></td><td style="font-size:12px">${fileSize(f.size)}</td></tr>`).join('')}
      </tbody></table></div>`}`;
}
window.applyStaged = async () => { if(!confirm('Apply?'))return; await apiPost('/staged/apply'); toast('Applied','success'); renderStaged(); };
window.discardStaged = async () => { if(!confirm('Discard?'))return; await apiPost('/staged/discard'); toast('Discarded'); renderStaged(); };
