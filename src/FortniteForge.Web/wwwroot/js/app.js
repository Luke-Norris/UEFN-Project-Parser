// FortniteForge Web Dashboard
const $ = (sel, ctx = document) => ctx.querySelector(sel);
const $$ = (sel, ctx = document) => [...ctx.querySelectorAll(sel)];
const content = () => $('#content');
const api = async (path) => { const r = await fetch(`/api${path}`); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const apiPost = async (path, body) => { const r = await fetch(`/api${path}`, { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) }); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const esc = s => s ? String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') : '';
const fileSize = b => b < 1024 ? b+' B' : b < 1048576 ? (b/1024).toFixed(1)+' KB' : (b/1048576).toFixed(1)+' MB';
const truncate = (s, max) => s && s.length > max ? s.slice(0, max) + '...' : (s || '');

function breadcrumb(items) {
  return `<nav class="breadcrumb">${items.map((item, i) =>
    i < items.length - 1
      ? `<a href="${item.href}">${esc(item.label)}</a><span class="bc-sep">/</span>`
      : `<span class="bc-current">${esc(item.label)}</span>`
  ).join('')}</nav>`;
}

function toast(msg, type = 'info') {
  const el = document.createElement('div');
  el.className = `toast toast-${type}`;
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => el.classList.add('show'), 10);
  setTimeout(() => { el.classList.remove('show'); setTimeout(() => el.remove(), 300); }, 3000);
}

// ========= Router =========
const routes = { '/': renderDashboard, '/levels': renderLevels, '/level': renderLevelDetail, '/assets': renderUserAssetsPage, '/epic-assets': renderEpicAssetsPage, '/asset': renderAssetDetail, '/stats': renderStats, '/audit': renderAudit, '/staged': renderStaged, '/library': renderLibrary, '/projects': renderProjects, '/device': renderDeviceDetail, '/device-type': renderDeviceType };

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

function clearCaches() { _assetCache = null; _epicAssetCache = null; _levelCache = {}; _status = null; }

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
  const levels = await api('/levels');
  const s = await getStatus();

  if (!levels.length) {
    content().innerHTML = `<div class="page-header"><h2>Levels</h2></div><div class="empty">No levels found in ${esc(s.projectName || 'this project')}</div>`;
    return;
  }

  content().innerHTML = `
    <div class="page-header"><h2>Levels</h2><div class="subtitle">${esc(s.projectName)} &mdash; ${levels.length} level(s)</div></div>
    <div class="table-wrapper"><table><thead><tr><th>Level</th><th></th></tr></thead><tbody>
      ${levels.map(l => `<tr>
        <td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a>
          <div style="font-size:10px;color:var(--text-muted)">${esc(l.relativePath)}</div></td>
        <td style="text-align:right"><a href="#/level?path=${encodeURIComponent(l.filePath)}&tab=devices" class="btn" style="font-size:11px">Devices</a></td>
      </tr>`).join('')}
    </tbody></table></div>`;
}

// ========= Level Detail =========
async function renderLevelDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const name = path.split(/[\\/]/).pop().replace(/\.\w+$/, '');
  const tab = params.get('tab') || 'devices';

  content().innerHTML = `
    ${breadcrumb([{ label: 'Dashboard', href: '#/' }, { label: name }])}
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
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
      <div style="font-size:12px;color:var(--text-secondary)">${d.deviceCount} devices across ${sorted.length} types</div>
      <a href="#/stats" style="font-size:11px;color:var(--accent);text-decoration:none">View statistics &rarr;</a>
    </div>
    <div class="search-bar" style="margin-bottom:12px"><input type="text" id="dev-search" placeholder="Search device types..."></div>
    <div class="table-wrapper"><table>
      <thead><tr><th>Device Type</th><th style="width:60px">Count</th><th style="width:60px"></th></tr></thead>
      <tbody id="dev-list">
        ${sorted.map(([cls, devs]) => `<tr data-search="${esc((devs[0].displayName+' '+cls).toLowerCase())}">
          <td><strong>${esc(devs[0].displayName)}</strong></td>
          <td style="text-align:center">${devs.length}</td>
          <td><a href="#/device-type?class=${encodeURIComponent(cls)}&level=${encodeURIComponent(path)}" class="btn" style="font-size:10px;padding:2px 8px">View</a></td>
        </tr>`).join('')}
      </tbody>
    </table></div>`;

  c.innerHTML = h;

  $('#dev-search')?.addEventListener('input', e => {
    const q = e.target.value.toLowerCase();
    $$('#dev-list tr').forEach(row => row.style.display = !q || row.dataset.search.includes(q) ? '' : 'none');
  });
}

async function renderActorsTab(c, path) {
  if (!_levelCache[path]) _levelCache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
  const d = _levelCache[path];
  const bd = d.staticActorBreakdown || [];

  c.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
      <div style="font-size:12px;color:var(--text-secondary)">${d.staticActorCount} static actors across ${bd.length} types</div>
      <a href="#/stats" style="font-size:11px;color:var(--accent);text-decoration:none">View charts &rarr;</a>
    </div>
    <div class="search-bar" style="margin-bottom:12px"><input type="text" id="actor-search" placeholder="Search actor types..."></div>
    <div class="table-wrapper"><table>
      <thead><tr><th>Actor Type</th><th style="width:60px">Count</th></tr></thead>
      <tbody id="actor-list">
        ${bd.map(cls => `<tr data-search="${esc((cls.displayName+' '+cls.className).toLowerCase())}">
          <td>${esc(cls.displayName)} <span style="font-size:10px;color:var(--text-muted)">${esc(cls.className)}</span></td>
          <td style="text-align:center">${cls.count}</td>
        </tr>`).join('')}
      </tbody>
    </table></div>`;

  $('#actor-search')?.addEventListener('input', e => {
    const q = e.target.value.toLowerCase();
    $$('#actor-list tr').forEach(row => row.style.display = !q || row.dataset.search.includes(q) ? '' : 'none');
  });
}

// ========= Device Detail =========
async function renderDeviceDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const dv = await api(`/device/inspect?path=${encodeURIComponent(path)}`);

  // Group properties by component
  const compGroups = {};
  dv.properties.forEach(p => {
    const key = p.componentClass || 'Other';
    (compGroups[key] = compGroups[key] || []).push(p);
  });

  // Sort: main actor component first, then by property count
  const mainClass = dv.className;
  const sortedGroups = Object.entries(compGroups).sort((a, b) => {
    if (a[0] === mainClass) return -1;
    if (b[0] === mainClass) return 1;
    return b[1].length - a[1].length;
  });

  const totalEditable = dv.properties.filter(p => p.isEditable).length;

  // Separate high-importance from noise within each group
  const renderPropTable = (props, editable) => {
    const high = props.filter(p => p.importance === 'high');
    const low = props.filter(p => p.importance === 'low');

    let html = '';
    if (high.length > 0) {
      html += `<table class="prop-table"><tbody>${high.map(p => renderPropRow(p, editable)).join('')}</tbody></table>`;
    }
    if (low.length > 0) {
      html += `<details class="prop-other-toggle"><summary>${low.length} rendering/engine properties</summary>
        <table class="prop-table"><tbody>${low.map(p => renderPropRow(p, false)).join('')}</tbody></table></details>`;
    }
    return html;
  };

  const renderPropRow = (p, editable) => {
    if (editable && p.isEditable) {
      const inputType = p.type === 'BoolProperty' ? 'checkbox' : p.type.match(/Int|Float|Double/) ? 'number' : 'text';
      const step = p.type.match(/Float|Double/) ? 'step="any"' : '';
      return `<tr data-prop="${esc(p.name)}" data-orig="${esc(p.value)}" data-type="${esc(p.type)}" data-comp="${esc(p.componentName)}">
        <td class="prop-td-name"><span class="override-dot" title="Non-default override"></span><span class="prop-name">${esc(p.name)}</span></td>
        <td class="prop-td-value">${inputType === 'checkbox'
          ? `<label class="toggle"><input type="checkbox" class="prop-edit" ${p.value==='True'?'checked':''} data-prop="${esc(p.name)}"> ${p.value}</label>`
          : `<input type="${inputType}" ${step} class="prop-edit prop-input" value="${esc(p.value)}" data-prop="${esc(p.name)}">`}</td>
        <td class="prop-td-type">${esc(p.type.replace('Property',''))}</td>
        <td class="prop-td-status"></td></tr>`;
    }
    return `<tr><td class="prop-td-name"><span class="override-dot"></span><span class="prop-name" style="color:var(--text-secondary)">${esc(p.name)}</span></td>
      <td class="prop-td-value" style="color:var(--text-muted);font-size:11px">${esc(truncate(p.value, 80))}</td>
      <td class="prop-td-type">${esc(p.type.replace('Property',''))}</td><td class="prop-td-status"></td></tr>`;
  };

  // Get level path for breadcrumb from the file path
  const pathParts = path.replace(/\\/g,'/').split('/');
  const extIdx = pathParts.indexOf('__ExternalActors__');
  const levelName = extIdx > 0 ? pathParts[extIdx + 1] : '';
  const contentIdx = pathParts.indexOf('Content');
  const levelPath = contentIdx > 0 ? pathParts.slice(0, contentIdx + 1).join('/') + '/' + levelName + '.umap' : '';

  content().innerHTML = `
    ${breadcrumb([
      { label: 'Dashboard', href: '#/' },
      ...(levelName ? [{ label: levelName, href: '#/level?path=' + encodeURIComponent(levelPath) }] : []),
      { label: dv.displayName }
    ])}

    <div class="page-header">
      <h2>${esc(dv.displayName)}</h2>
      <div class="subtitle">${esc(dv.className)}</div>
    </div>

    <div class="card-grid" style="margin-bottom:16px">
      ${dv.hasPosition ? `<div class="card"><div class="card-label">Position</div><div style="font-size:12px">(${dv.x.toFixed(0)}, ${dv.y.toFixed(0)}, ${dv.z.toFixed(0)})</div></div>` : ''}
      <div class="card"><div class="card-label">Components</div><div class="card-value purple">${dv.components.length}</div></div>
      <div class="card"><div class="card-label">Config Fields</div><div class="card-value green">${totalEditable}</div></div>
    </div>

    <div id="pending-banner"></div>

    <div id="prop-groups">
      ${sortedGroups.map(([compClass, props]) => {
        const editable = props.filter(p => p.isEditable);
        const nonEditable = props.filter(p => !p.isEditable);
        const isMain = compClass === mainClass;
        const cleanClass = DeviceClassifier_cleanName(compClass);
        const highCount = props.filter(p => p.importance === 'high').length;

        return `<details class="prop-group" ${isMain ? 'open' : ''}>
          <summary class="prop-group-header">
            <span class="prop-group-title">
              ${isMain ? '<span class="badge badge-blue" style="font-size:10px">Main</span>' : '<span class="badge badge-purple" style="font-size:10px">Component</span>'}
              ${esc(cleanClass)}
            </span>
            <span class="prop-group-count">${highCount} config${editable.length !== highCount ? ` / ${props.length} total` : ''}</span>
          </summary>
          <div class="prop-group-body">
            ${renderPropTable(editable, true)}
            ${nonEditable.length > 0 ? `<details class="prop-other-toggle"><summary>${nonEditable.length} non-editable properties</summary>
              ${renderPropTable(nonEditable, false)}</details>` : ''}
          </div>
        </details>`;
      }).join('')}
    </div>

    <div style="margin-top:20px;font-size:10px;color:var(--text-muted)">${esc(path)}</div>`;

  // Wire editing
  $$('.prop-edit').forEach(input => {
    input.addEventListener('change', async () => {
      const row = input.closest('tr');
      const propName = input.dataset.prop;
      const origVal = row.dataset.orig;
      const compName = row.dataset.comp;
      const newVal = input.type === 'checkbox' ? (input.checked ? 'True' : 'False') : input.value;
      const status = row.querySelector('.prop-td-status');

      if (newVal !== origVal) {
        status.innerHTML = '<span class="change-dot" title="Modified"></span>';
        input.style.borderColor = 'var(--yellow)';
        await apiPost('/changes/add', { filePath: path, exportName: compName || '', propertyName: propName, oldValue: origVal, newValue: newVal, propertyType: row.dataset.type, deviceName: dv.displayName });
      } else {
        status.innerHTML = '';
        input.style.borderColor = '';
        await apiPost('/changes/remove', { filePath: path, exportName: '', propertyName: propName });
      }
      refreshPendingBanner();
    });
  });
  refreshPendingBanner();
}

// Clean component class name for display
function DeviceClassifier_cleanName(cls) {
  return cls.replace('Device_','').replace(/_C$/,'').replace(/_/g,' ').replace(/  /g,' ').trim();
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

// ========= Device Type Browser =========
async function renderDeviceType(params) {
  const className = params.get('class');
  if (!className) return;

  let levelPath = params.get('level');
  if (!levelPath) {
    try { const lvls = await api('/levels'); if (lvls.length) levelPath = lvls[0].filePath; } catch {}
  }
  if (!levelPath) { content().innerHTML = '<div class="empty">No level found</div>'; return; }

  const levelName = levelPath.split(/[\\/]/).pop().replace(/\.\w+$/,'');
  content().innerHTML = '<div class="loading"><div class="spinner"></div></div>';

  const data = await api(`/levels/devices-by-class?levelPath=${encodeURIComponent(levelPath)}&className=${encodeURIComponent(className)}`);

  content().innerHTML = `
    ${breadcrumb([
      { label: 'Dashboard', href: '#/' },
      { label: levelName, href: '#/level?path=' + encodeURIComponent(levelPath) },
      { label: 'Devices', href: '#/level?path=' + encodeURIComponent(levelPath) + '&tab=devices' },
      { label: data.displayName }
    ])}
    <div class="page-header">
      <h2>${esc(data.displayName)}</h2>
      <div class="subtitle">${data.count} instance(s) ${data.isDevice ? '<span class="badge badge-blue">Device</span>' : '<span class="badge badge-dim">Prop</span>'}</div>
    </div>

    <div class="search-bar"><input type="text" id="instance-search" placeholder="Search instances..."></div>

    <div class="table-wrapper"><table>
      <thead><tr><th>Name</th><th style="width:80px">Editable</th><th style="width:60px"></th></tr></thead>
      <tbody id="instance-list">
        ${data.instances.map(inst => `
          <tr data-search="${esc(inst.label.toLowerCase())}">
            <td><strong>${esc(inst.label)}</strong></td>
            <td style="color:var(--text-muted);font-size:12px">${inst.editableCount}</td>
            <td><a href="#/device?path=${encodeURIComponent(inst.filePath)}" class="btn" style="font-size:10px;padding:2px 8px">View</a></td>
          </tr>
        `).join('')}
      </tbody>
    </table></div>
  `;

  $('#instance-search')?.addEventListener('input', e => {
    const q = e.target.value.toLowerCase();
    $$('#instance-list tr').forEach(row => row.style.display = !q || row.dataset.search.includes(q) ? '' : 'none');
  });
}

// ========= User Assets Page =========
let _epicAssetCache = null;

async function renderUserAssetsPage() {
  content().innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  if (!_assetCache) _assetCache = await api('/assets');
  const s = await getStatus();

  if (!_assetCache.length) {
    content().innerHTML = `<div class="page-header"><h2>User Assets</h2><div class="subtitle">${esc(s.projectName)}</div></div>
      <div class="empty">No user-created assets in this project.</div>`;
    return;
  }

  const classes = [...new Set(_assetCache.map(a => a.assetClass))].sort();
  content().innerHTML = `
    <div class="page-header"><h2>User Assets</h2><div class="subtitle">${_assetCache.length} assets in ${esc(s.projectName)}</div></div>
    <div style="display:flex;gap:8px;align-items:center;margin-bottom:12px">
      <div class="search-bar" style="flex:1;margin-bottom:0">
        <input type="text" id="asset-search" placeholder="Search assets...">
        <select id="asset-class-filter" style="background:var(--bg-tertiary);border:1px solid var(--border);border-radius:4px;padding:4px 8px;color:var(--text-primary);font-size:12px">
          <option value="">All Types (${_assetCache.length})</option>
          ${classes.map(c => `<option value="${esc(c)}">${esc(c)} (${_assetCache.filter(a=>a.assetClass===c).length})</option>`).join('')}
        </select>
      </div>
      <div style="display:flex;gap:4px">
        <button class="btn view-btn active" data-view="grid" style="font-size:11px;padding:3px 8px">Grid</button>
        <button class="btn view-btn" data-view="list" style="font-size:11px;padding:3px 8px">List</button>
      </div>
    </div>
    <div id="asset-list"></div>`;

  let viewMode = 'grid';

  const renderGrid = (f) => {
    if (!f.length) { $('#asset-list').innerHTML = '<div class="empty">No matches</div>'; return; }
    $('#asset-list').innerHTML = `<div class="asset-grid">${f.slice(0, 200).map(a =>
      `<a href="#/asset?path=${encodeURIComponent(a.filePath)}" class="asset-tile" title="${esc(a.name)}\n${esc(a.assetClass)}">
        <div class="asset-tile-thumb">${a.hasThumbnail
          ? `<img src="/api/assets/thumbnail?path=${encodeURIComponent(a.filePath)}" loading="lazy">`
          : `<span class="thumb-placeholder">${esc(a.assetClass.substring(0, 3))}</span>`}</div>
        <div class="asset-tile-name">${esc(a.name)}</div>
        <div class="asset-tile-class">${esc(a.assetClass)}</div>
      </a>`).join('')}</div>
    ${f.length > 200 ? `<div style="text-align:center;padding:8px;color:var(--text-muted);font-size:11px">Showing 200 of ${f.length}</div>` : ''}`;
  };

  const renderList = (f) => {
    if (!f.length) { $('#asset-list').innerHTML = '<div class="empty">No matches</div>'; return; }
    const groups = {}; f.forEach(a => (groups[a.assetClass] = groups[a.assetClass] || []).push(a));
    const sorted = Object.entries(groups).sort((a,b) => b[1].length - a[1].length);
    let html = '';
    sorted.forEach(([cls, assets]) => {
      html += `<div class="device-group" style="margin-bottom:6px"><div class="device-group-header" onclick="this.parentElement.classList.toggle('collapsed')">
        <span class="tree-toggle">&#9662;</span><span class="badge badge-purple">${esc(cls)}</span><span class="tree-count">${assets.length}</span></div>
        <div class="device-group-items"><div class="table-wrapper" style="margin:0"><table><tbody>
          ${assets.slice(0,50).map(a => `<tr>
            <td style="width:32px;padding:4px">${a.hasThumbnail ? `<img src="/api/assets/thumbnail?path=${encodeURIComponent(a.filePath)}" style="width:28px;height:28px;border-radius:3px;object-fit:cover" loading="lazy">` : ''}</td>
            <td><a href="#/asset?path=${encodeURIComponent(a.filePath)}">${esc(a.name)}</a></td>
            <td style="font-size:11px;color:var(--text-muted);width:80px">${fileSize(a.fileSize)}</td></tr>`).join('')}
        </tbody></table></div></div></div>`;
    });
    $('#asset-list').innerHTML = html;
  };

  const render = (f) => { if (viewMode === 'grid') renderGrid(f); else renderList(f); };
  const filter = () => {
    const q = $('#asset-search').value.toLowerCase(), cls = $('#asset-class-filter')?.value || '';
    render(_assetCache.filter(a => (!q || a.name.toLowerCase().includes(q) || a.assetClass.toLowerCase().includes(q)) && (!cls || a.assetClass === cls)));
  };

  $$('.view-btn').forEach(btn => btn.addEventListener('click', () => {
    $$('.view-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    viewMode = btn.dataset.view;
    filter();
  }));

  $('#asset-search').addEventListener('input', filter);
  $('#asset-class-filter')?.addEventListener('change', filter);
  filter();
}

async function renderEpicAssetsPage() {
  content().innerHTML = '<div class="loading"><div class="spinner"></div> Scanning placed actors...</div>';
  if (!_epicAssetCache) _epicAssetCache = await api('/assets/epic');
  const s = await getStatus();
  if (!_epicAssetCache.length) { content().innerHTML = `<div class="page-header"><h2>Epic Assets</h2></div><div class="empty">No placed actors found</div>`; return; }

  const devices = _epicAssetCache.filter(a => a.isDevice);
  const props = _epicAssetCache.filter(a => !a.isDevice);
  const total = _epicAssetCache.reduce((s, a) => s + a.count, 0);
  const pal = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba','#79c0ff','#7ee787'];

  const renderTable = (items) => {
    if (!items.length) return '<div style="color:var(--text-muted);font-size:12px;padding:8px">None</div>';
    return `<div class="table-wrapper"><table><thead><tr><th>Asset Type</th><th style="width:60px">Count</th><th style="width:60px"></th></tr></thead><tbody>
      ${items.map(a => `<tr data-search="${esc((a.displayName+' '+a.className).toLowerCase())}">
        <td><strong>${esc(a.displayName)}</strong></td>
        <td style="text-align:center">${a.count}</td>
        <td>${a.samplePaths?.length ? `<a href="#/device-type?class=${encodeURIComponent(a.className)}" class="btn" style="font-size:10px;padding:2px 8px">View</a>` : ''}</td>
      </tr>`).join('')}
    </tbody></table></div>`;
  };

  content().innerHTML = `
    <div class="page-header"><h2>Epic Assets</h2><div class="subtitle">${total.toLocaleString()} placed instances in ${esc(s.projectName)}</div></div>
    <div class="card-grid" style="margin-bottom:16px">
      <div class="card"><div class="card-label">Total Placed</div><div class="card-value accent">${total.toLocaleString()}</div></div>
      <div class="card"><div class="card-label">Asset Types</div><div class="card-value purple">${_epicAssetCache.length}</div></div>
      <div class="card"><div class="card-label">Devices</div><div class="card-value green">${devices.reduce((s,d)=>s+d.count,0)}</div></div>
      <div class="card"><div class="card-label">Props/Terrain</div><div class="card-value">${props.reduce((s,p)=>s+p.count,0)}</div></div>
    </div>
    <div class="search-bar" style="margin-bottom:12px"><input type="text" id="epic-search" placeholder="Search asset types..."></div>
    ${devices.length ? `<h3 style="margin:12px 0 8px">Devices <span class="tree-count">${devices.length} types</span></h3><div id="epic-devices">${renderTable(devices)}</div>` : ''}
    <h3 style="margin:12px 0 8px">Props &amp; Terrain <span class="tree-count">${props.length} types</span></h3>
    <div id="epic-props">${renderTable(props)}</div>`;

  $('#epic-search')?.addEventListener('input', e => {
    const q = e.target.value.toLowerCase();
    $$('#epic-devices tr, #epic-props tr').forEach(row => {
      if (row.dataset.search) row.style.display = !q || row.dataset.search.includes(q) ? '' : 'none';
    });
  });
}

async function renderAssetDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const d = await api(`/assets/inspect?path=${encodeURIComponent(path)}`);

  content().innerHTML = `
    ${breadcrumb([{ label: 'Dashboard', href: '#/' }, { label: 'Assets', href: '#/assets' }, { label: d.name }])}
    <div style="display:flex;gap:24px;margin-bottom:20px">
      <div class="asset-thumb" id="asset-thumb"></div>
      <div style="flex:1">
        <div class="page-header" style="margin-bottom:12px"><h2>${esc(d.name)}</h2><div class="subtitle">${esc(d.relativePath)}</div></div>
        <div class="card-grid">
          <div class="card"><div class="card-label">Class</div><div style="font-size:12px;color:var(--accent)">${esc(d.assetClass)}</div></div>
          <div class="card"><div class="card-label">Size</div><div style="font-size:12px">${fileSize(d.fileSize)}</div></div>
          <div class="card"><div class="card-label">Exports</div><div class="card-value purple">${d.exportCount}</div></div>
          <div class="card"><div class="card-label">Imports</div><div class="card-value">${d.importCount}</div></div>
        </div>
      </div>
    </div>
    <h3 style="margin-bottom:8px">Exports</h3>
    ${(d.exports||[]).map(e => `<details class="prop-group">
      <summary class="prop-group-header">
        <span class="prop-group-title"><span class="badge badge-purple" style="font-size:10px">${esc(e.className)}</span> ${esc(e.objectName)}</span>
        <span class="prop-group-count">${e.properties?.length||0} props</span></summary>
      <div class="prop-group-body">
      ${(e.properties?.length > 0) ? `<table class="prop-table"><tbody>
        ${e.properties.map(p => `<tr><td class="prop-td-name"><span class="prop-name">${esc(p.name)}</span></td>
          <td class="prop-td-value" style="font-size:11px">${esc(truncate(p.value,100))}</td>
          <td class="prop-td-type">${esc(p.type)}</td></tr>`).join('')}
      </tbody></table>` : '<div style="padding:8px 10px;font-size:11px;color:var(--text-muted)">No properties</div>'}
      </div>
    </details>`).join('')}`;

  // Load thumbnail
  const thumb = $('#asset-thumb');
  try {
    const res = await fetch(`/api/assets/thumbnail?path=${encodeURIComponent(path)}`);
    if (res.ok && res.headers.get('content-type')?.includes('image')) {
      const blob = await res.blob();
      const img = document.createElement('img');
      img.src = URL.createObjectURL(blob);
      img.className = 'thumb-img';
      thumb.appendChild(img);
    } else {
      thumb.innerHTML = '<div class="thumb-placeholder">No Preview</div>';
    }
  } catch { thumb.innerHTML = '<div class="thumb-placeholder">No Preview</div>'; }
}

// ========= Library =========
async function renderLibrary() {
  content().innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  const status = await api('/library/status');

  if (!status.indexed) {
    content().innerHTML = `
      <div class="page-header"><h2>Library</h2><div class="subtitle">UEFN asset and verse reference library</div></div>
      <div class="card" style="max-width:500px">
        <p style="margin-bottom:12px;font-size:13px">Build a searchable index of all UEFN projects, verse files, and assets in your map collection.</p>
        <div class="search-bar" style="margin-bottom:8px">
          <input type="text" id="lib-path" placeholder="Path to UEFN library" value="Z:\\UEFN_Resources\\mapContent">
          <button class="btn" id="lib-browse">Browse</button>
        </div>
        <button class="btn btn-primary" id="lib-build">Build Library Index</button>
        <div id="lib-status" style="margin-top:8px"></div>
      </div>`;

    $('#lib-browse').addEventListener('click', () => pickFolder('lib-path'));
    $('#lib-build').addEventListener('click', async () => {
      const path = $('#lib-path').value.trim();
      if (!path) return;
      $('#lib-status').innerHTML = '<div class="loading" style="padding:8px"><div class="spinner"></div> Indexing (this may take a few minutes)...</div>';
      try {
        const result = await apiPost('/library/build', { path });
        toast(`Library indexed: ${result.totalVerseFiles} verse, ${result.totalAssets} assets`, 'success');
        _currentRoute = null; renderLibrary();
      } catch (err) { $('#lib-status').innerHTML = `<div style="color:var(--red);font-size:12px">${esc(err.message)}</div>`; }
    });
    return;
  }

  // Library is indexed — show search + browse
  content().innerHTML = `
    <div class="page-header"><h2>Library</h2><div class="subtitle">Indexed ${status.indexedAt ? new Date(status.indexedAt).toLocaleString() : ''} &mdash; ${status.projectCount} projects</div></div>
    <div class="card-grid" style="margin-bottom:16px">
      <div class="card"><div class="card-label">Verse Files</div><div class="card-value accent">${status.totalVerseFiles}</div></div>
      <div class="card"><div class="card-label">Assets</div><div class="card-value purple">${status.totalAssets}</div></div>
      <div class="card"><div class="card-label">Device Types</div><div class="card-value green">${status.totalDeviceTypes}</div></div>
      <div class="card"><div class="card-label">Projects</div><div class="card-value">${status.projectCount}</div></div>
    </div>

    <div class="search-bar" style="margin-bottom:16px">
      <input type="text" id="lib-search" placeholder="Search library (e.g. ranked system, vending machine, damage volume)...">
      <button class="btn btn-primary" id="lib-search-btn">Search</button>
    </div>
    <div id="lib-results"></div>

    <div style="margin-top:24px;display:flex;gap:8px">
      <button class="btn" id="lib-verse-btn">Browse Verse Files</button>
      <button class="btn" id="lib-mat-btn">Browse Materials</button>
      <button class="btn btn-danger" style="font-size:11px" id="lib-rebuild">Rebuild Index</button>
    </div>
    <div id="lib-browse-results" style="margin-top:16px"></div>
  `;

  const doSearch = async () => {
    const q = $('#lib-search').value.trim();
    if (!q) return;
    $('#lib-results').innerHTML = '<div class="loading" style="padding:8px"><div class="spinner"></div></div>';
    const result = await api(`/library/search?q=${encodeURIComponent(q)}`);

    let html = '';
    if (result.verseFiles.length) {
      html += `<h3 style="margin:12px 0 8px">Verse Files (${result.verseFiles.length})</h3>
        <div class="table-wrapper"><table><thead><tr><th>File</th><th>Project</th><th>Lines</th><th>Summary</th></tr></thead><tbody>
          ${result.verseFiles.map(h => `<tr>
            <td><a href="#" onclick="viewVerseSource('${esc(h.item.filePath).replace(/\\/g,'\\\\').replace(/'/g,"\\'")}');return false">${esc(h.item.name)}</a></td>
            <td><span class="badge badge-purple">${esc(h.projectName)}</span></td>
            <td>${h.item.lineCount}</td>
            <td style="font-size:11px;color:var(--text-secondary)">${esc(h.item.summary)}</td>
          </tr>`).join('')}
        </tbody></table></div>`;
    }
    if (result.assets.length) {
      html += `<h3 style="margin:12px 0 8px">Assets (${result.assets.length})</h3>
        <div class="table-wrapper"><table><thead><tr><th>Asset</th><th>Project</th><th>Type</th></tr></thead><tbody>
          ${result.assets.map(h => `<tr>
            <td>${esc(h.item.name)}</td>
            <td><span class="badge badge-purple">${esc(h.projectName)}</span></td>
            <td><span class="badge badge-dim">${esc(h.item.assetClass)}</span></td>
          </tr>`).join('')}
        </tbody></table></div>`;
    }
    if (result.deviceTypes.length) {
      html += `<h3 style="margin:12px 0 8px">Device Types (${result.deviceTypes.length})</h3>
        <div class="table-wrapper"><table><thead><tr><th>Device</th><th>Project</th><th>Count</th></tr></thead><tbody>
          ${result.deviceTypes.map(h => `<tr>
            <td>${esc(h.item.displayName)}</td>
            <td><span class="badge badge-purple">${esc(h.projectName)}</span></td>
            <td>${h.item.count}</td>
          </tr>`).join('')}
        </tbody></table></div>`;
    }
    if (!html) html = '<div class="empty">No results found</div>';
    $('#lib-results').innerHTML = html;
  };

  $('#lib-search-btn').addEventListener('click', doSearch);
  $('#lib-search').addEventListener('keydown', e => { if (e.key === 'Enter') doSearch(); });

  $('#lib-verse-btn').addEventListener('click', async () => {
    $('#lib-browse-results').innerHTML = '<div class="loading" style="padding:8px"><div class="spinner"></div></div>';
    const files = await api('/library/verse');
    $('#lib-browse-results').innerHTML = `<h3 style="margin-bottom:8px">All Verse Files (${files.length})</h3>
      <div class="search-bar" style="margin-bottom:8px"><input type="text" id="verse-filter" placeholder="Filter verse files..."></div>
      <div class="table-wrapper"><table><thead><tr><th>File</th><th>Project</th><th>Lines</th><th style="width:120px"></th></tr></thead><tbody id="verse-list">
        ${files.map((f, i) => `<tr data-search="${esc((f.name+' '+f.projectName+' '+(f.summary||'')+' '+(f.classes||[]).join(' ')+' '+(f.functions||[]).join(' ')).toLowerCase())}">
          <td><a href="#" onclick="viewVerseSource('${esc(f.filePath).replace(/\\/g,'\\\\').replace(/'/g,"\\'")}');return false">${esc(f.name)}</a></td>
          <td><span class="badge badge-purple">${esc(f.projectName)}</span></td>
          <td>${f.lineCount}</td>
          <td><button class="btn" style="font-size:10px;padding:2px 6px" onclick="toggleVerseSummary(this,${i})">Summary</button></td>
        </tr>
        <tr class="verse-summary-row" id="vs-${i}" style="display:none" data-search="${esc((f.name+' '+f.projectName).toLowerCase())}">
          <td colspan="4" style="padding:8px 16px;background:var(--bg-tertiary);border:none">
            ${(f.classes||[]).length ? `<div style="margin-bottom:4px"><span style="font-size:10px;color:var(--text-muted)">Classes:</span> ${f.classes.map(c => `<span class="badge badge-blue" style="font-size:10px;margin-right:4px">${esc(c)}</span>`).join('')}</div>` : ''}
            ${(f.functions||[]).length ? `<div style="margin-bottom:4px"><span style="font-size:10px;color:var(--text-muted)">Functions:</span> ${f.functions.map(fn => `<span class="badge badge-green" style="font-size:10px;margin-right:4px">${esc(fn)}</span>`).join('')}</div>` : ''}
            ${(f.deviceReferences||[]).length ? `<div><span style="font-size:10px;color:var(--text-muted)">Devices:</span> ${f.deviceReferences.map(d => `<span class="badge badge-purple" style="font-size:10px;margin-right:4px">${esc(d)}</span>`).join('')}</div>` : ''}
            ${!(f.classes||[]).length && !(f.functions||[]).length ? '<div style="font-size:11px;color:var(--text-muted)">No classes or functions detected</div>' : ''}
          </td>
        </tr>`).join('')}
      </tbody></table></div>`;
    $('#verse-filter')?.addEventListener('input', e => {
      const q = e.target.value.toLowerCase();
      $$('#verse-list tr:not(.verse-summary-row)').forEach(r => {
        const show = !q || r.dataset.search.includes(q);
        r.style.display = show ? '' : 'none';
        // Also hide corresponding summary row
        const next = r.nextElementSibling;
        if (next?.classList.contains('verse-summary-row') && !show) next.style.display = 'none';
      });
    });
  });

  $('#lib-mat-btn').addEventListener('click', async () => {
    $('#lib-browse-results').innerHTML = '<div class="loading" style="padding:8px"><div class="spinner"></div></div>';
    const mats = await api('/library/materials');
    $('#lib-browse-results').innerHTML = `<h3 style="margin-bottom:8px">Materials (${mats.length})</h3>
      <div class="table-wrapper"><table><thead><tr><th>Material</th><th>Type</th><th>Path</th></tr></thead><tbody>
        ${mats.map(m => `<tr><td>${esc(m.name)}</td><td><span class="badge badge-dim">${esc(m.assetClass)}</span></td><td style="font-size:11px;color:var(--text-muted)">${esc(m.relativePath)}</td></tr>`).join('')}
      </tbody></table></div>`;
  });

  $('#lib-rebuild').addEventListener('click', async () => {
    const path = prompt('Library path:', status.libraryPath || 'Z:\\UEFN_Resources\\mapContent\\map_resources');
    if (!path) return;
    toast('Rebuilding library index...', 'info');
    try {
      await apiPost('/library/build', { path });
      toast('Library rebuilt', 'success');
      _currentRoute = null; renderLibrary();
    } catch (err) { toast(err.message, 'error'); }
  });
}

window.toggleVerseSummary = (btn, idx) => {
  const row = $(`#vs-${idx}`);
  if (row) { row.style.display = row.style.display === 'none' ? '' : 'none'; }
};

window.viewVerseSource = async (path) => {
  try {
    const data = await api(`/library/verse/source?path=${encodeURIComponent(path)}`);
    content().innerHTML = `
      ${breadcrumb([{ label: 'Library', href: '#/library' }, { label: data.name }])}
      <div class="page-header"><h2>${esc(data.name)}</h2></div>
      <div class="verse-source"><pre><code>${highlightVerse(data.source)}</code></pre></div>`;
  } catch (err) { toast(err.message, 'error'); }
};

function highlightVerse(source) {
  // Verse syntax highlighting
  const keywords = ['using','var','let','set','if','else','for','loop','return','break','class','interface','struct','enum','module','where','case','then','do','block','spawn','sync','rush','race','branch','defer','not','and','or','true','false','self','super','new','array','map','option','logic','int','float','string','char','void','type','of','is','extends'];
  const builtins = ['Print','Sleep','Await','MakeMessage','ToString','Log','GetPlayspace','GetPlayers','Eliminate','Respawn','Enable','Disable','Activate','Deactivate','Subscribe','Signal','Send','Receive','SetText','GetTransform','SetTransform','TeleportTo','MoveTo'];

  let result = esc(source);

  // Comments (# line comments)
  result = result.replace(/(#[^\n]*)/g, '<span class="vs-comment">$1</span>');

  // Strings
  result = result.replace(/(&quot;[^&]*?&quot;)/g, '<span class="vs-string">$1</span>');

  // Numbers
  result = result.replace(/\b(\d+\.?\d*)\b/g, '<span class="vs-number">$1</span>');

  // Keywords
  const kwPattern = new RegExp(`\\b(${keywords.join('|')})\\b`, 'g');
  result = result.replace(kwPattern, '<span class="vs-keyword">$1</span>');

  // Builtins
  const biPattern = new RegExp(`\\b(${builtins.join('|')})\\b`, 'g');
  result = result.replace(biPattern, '<span class="vs-builtin">$1</span>');

  // Decorators (@editable, @replicated)
  result = result.replace(/(@\w+)/g, '<span class="vs-decorator">$1</span>');

  // Type annotations after :
  result = result.replace(/:(\s*)([\w_]+)/g, ':<span class="vs-type">$1$2</span>');

  return result;
}

// ========= Statistics =========
async function renderStats() {
  const s = await getStatus();
  if (!s.projectName || s.projectName === 'No Project') {
    content().innerHTML = '<div class="empty">No active project. <a href="#/projects">Add one</a></div>';
    return;
  }

  content().innerHTML = `${breadcrumb([{ label: 'Dashboard', href: '#/' }, { label: 'Statistics' }])}
    <div class="page-header"><h2>Statistics</h2><div class="subtitle">${esc(s.projectName)}</div></div>
    <div id="stats-content"><div class="loading"><div class="spinner"></div> Analyzing project...</div></div>`;

  let levels = []; try { levels = await api('/levels'); } catch {}
  const sc = $('#stats-content');

  // Overview cards
  let html = `<div class="card-grid" style="margin-bottom:24px">
    <div class="card"><div class="card-label">Definitions</div><div class="card-value accent">${s.definitionCount || 0}</div></div>
    <div class="card"><div class="card-label">Placed Actors</div><div class="card-value purple">${((s.assetCount||0) - (s.definitionCount||0)).toLocaleString()}</div></div>
    <div class="card"><div class="card-label">Levels</div><div class="card-value">${levels.length}</div></div>
    <div class="card"><div class="card-label">Verse Files</div><div class="card-value green">${s.verseCount || 0}</div></div>
  </div>`;

  // Load level data for charts
  if (levels.length > 0) {
    const path = levels[0].filePath;
    try {
      if (!_levelCache[path]) _levelCache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
      const d = _levelCache[path];
      const groups = {};
      d.devices.forEach(dv => (groups[dv.className] = groups[dv.className] || []).push(dv));
      const deviceTypes = Object.entries(groups).sort((a,b) => b[1].length - a[1].length);
      const bd = d.staticActorBreakdown || [];
      const pal = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba','#79c0ff','#7ee787'];

      // Device distribution chart
      if (deviceTypes.length > 0) {
        const maxD = deviceTypes[0][1].length;
        html += `<h3 style="margin-bottom:10px">Device Distribution <span class="tree-count">${d.deviceCount} devices, ${deviceTypes.length} types</span></h3>
          <div style="display:flex;flex-direction:column;gap:3px;margin-bottom:28px">${deviceTypes.map(([cls, devs], i) => {
            const pct = (devs.length / maxD * 100).toFixed(0);
            return `<div style="display:flex;align-items:center;gap:10px">
              <div style="width:180px;font-size:11px;text-align:right;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(devs[0].displayName)}</div>
              <div style="flex:1;height:18px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden"><div style="width:${pct}%;height:100%;background:${pal[i%pal.length]};border-radius:3px;min-width:2px"></div></div>
              <div style="width:36px;font-size:11px;font-weight:600;text-align:right">${devs.length}</div></div>`;
          }).join('')}</div>`;
      }

      // Static actor distribution chart
      if (bd.length > 0) {
        const maxA = bd[0].count;
        html += `<h3 style="margin-bottom:10px">Static Actor Distribution <span class="tree-count">${d.staticActorCount} actors, ${bd.length} types</span></h3>
          <div style="display:flex;flex-direction:column;gap:3px;margin-bottom:28px">${bd.slice(0, 30).map((cls, i) => {
            const pct = (cls.count / maxA * 100).toFixed(0);
            return `<div style="display:flex;align-items:center;gap:10px">
              <div style="width:180px;font-size:11px;text-align:right;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(cls.displayName)}</div>
              <div style="flex:1;height:18px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden"><div style="width:${pct}%;height:100%;background:${pal[i%pal.length]};border-radius:3px;min-width:2px"></div></div>
              <div style="width:36px;font-size:11px;font-weight:600;text-align:right">${cls.count}</div></div>`;
          }).join('')}${bd.length > 30 ? `<div style="font-size:11px;color:var(--text-muted);text-align:center;padding:4px">+${bd.length - 30} more types</div>` : ''}</div>`;
      }

      // Composition pie (text version)
      const devicePct = ((d.deviceCount / d.totalActorFiles) * 100).toFixed(1);
      const staticPct = ((d.staticActorCount / d.totalActorFiles) * 100).toFixed(1);
      html += `<h3 style="margin-bottom:10px">Level Composition</h3>
        <div class="card-grid">
          <div class="card"><div class="card-label">Devices</div><div class="card-value accent">${d.deviceCount}</div><div style="font-size:11px;color:var(--text-muted)">${devicePct}%</div></div>
          <div class="card"><div class="card-label">Static Props</div><div class="card-value">${d.staticActorCount}</div><div style="font-size:11px;color:var(--text-muted)">${staticPct}%</div></div>
          <div class="card"><div class="card-label">Parse Errors</div><div class="card-value ${d.parseErrors > 0 ? 'red' : ''}">${d.parseErrors}</div></div>
        </div>`;

    } catch (err) {
      html += `<div class="empty">Could not load level data: ${esc(err.message)}</div>`;
    }
  }

  sc.innerHTML = html;
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
