// FortniteForge Web Dashboard
const $ = (sel, ctx = document) => ctx.querySelector(sel);
const $$ = (sel, ctx = document) => [...ctx.querySelectorAll(sel)];
const content = () => $('#content');
const api = async (path) => { const r = await fetch(`/api${path}`); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const apiPost = async (path, body) => { const r = await fetch(`/api${path}`, { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) }); if (!r.ok) throw new Error(await r.text()); return r.json(); };
const esc = s => s ? String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') : '';
const fileSize = b => b < 1024 ? b+' B' : b < 1048576 ? (b/1024).toFixed(1)+' KB' : (b/1048576).toFixed(1)+' MB';
const truncate = (s, max) => s && s.length > max ? s.slice(0, max) + '...' : (s || '');

// ========= Router =========
const routes = { '/': renderDashboard, '/levels': renderLevels, '/level': renderLevelDetail, '/assets': renderAssets, '/asset': renderAssetDetail, '/audit': renderAudit, '/staged': renderStaged, '/projects': renderProjects, '/device': renderDeviceDetail };

function navigate() {
  const hash = location.hash.slice(1) || '/';
  const [path, ...pp] = hash.split('?');
  const params = new URLSearchParams(pp.join('?'));
  $$('.sidebar-nav a').forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + path || (a.dataset.page === 'dashboard' && path === '/')));
  content().innerHTML = '<div class="loading"><div class="spinner"></div> Loading...</div>';
  (routes[path] || routes['/'])(params).catch(err => { content().innerHTML = `<div class="empty">Error: ${esc(err.message)}</div>`; });
}
window.addEventListener('hashchange', navigate);

// ========= Init =========
let statusData = null;
let projectsData = null;

async function init() {
  await refreshSidebar();
  navigate();
}

async function refreshSidebar() {
  try {
    projectsData = await api('/projects');
    statusData = await api('/status');

    const active = projectsData.projects.find(p => p.id === projectsData.activeProjectId);
    if (active) {
      const typeLabel = active.type === 'Library' ? 'Library' : 'My Project';
      $('#sidebar-project').innerHTML = `${esc(active.name)} <span style="font-size:10px;color:var(--text-muted)">${typeLabel}</span>`;
    } else {
      $('#sidebar-project').innerHTML = '<a href="#/projects" style="color:var(--yellow)">Add a project</a>';
    }

    const mc = { ReadOnly: 'red', Staged: 'yellow', Direct: 'green' };
    const mode = statusData.mode || 'None';
    $('#sidebar-status').innerHTML = active ? `
      <div><span class="indicator indicator-${statusData.isUefnRunning ? 'green' : 'dim'}"></span>UEFN: ${statusData.isUefnRunning ? 'Running' : 'Off'}</div>
      <div style="margin-top:4px"><span class="indicator indicator-${mc[mode]||'dim'}"></span>Mode: ${mode}</div>
      <div style="margin-top:4px;font-size:10px;color:var(--text-muted)">${projectsData.projects.length} project(s)</div>
    ` : '';
  } catch (err) {
    $('#sidebar-project').textContent = 'Not connected';
  }
}

init();

// ========= Projects (the home base) =========
async function renderProjects() {
  const data = await api('/projects');
  const projects = data.projects;

  content().innerHTML = `
    <div class="page-header"><h2>Projects</h2><div class="subtitle">Manage your UEFN projects</div></div>

    ${projects.length > 0 ? `
      <div class="table-wrapper" style="margin-bottom:24px"><table>
        <thead><tr><th>Project</th><th>Type</th><th>Assets</th><th>Devices</th><th>Verse</th><th>Actions</th></tr></thead>
        <tbody>
          ${projects.map(p => `<tr class="${p.id === data.activeProjectId ? 'active-row' : ''}">
            <td>
              <strong>${esc(p.name)}</strong>
              ${p.id === data.activeProjectId ? '<span class="badge badge-green" style="margin-left:6px">Active</span>' : ''}
              ${p.isUefnProject ? '<span class="badge badge-blue" style="margin-left:4px">UEFN</span>' : ''}
              ${p.hasUrc ? '<span class="badge badge-yellow" style="margin-left:4px">URC</span>' : ''}
              <div style="font-size:11px;color:var(--text-muted);margin-top:2px">${esc(p.projectPath)}</div>
            </td>
            <td><span class="badge ${p.type === 'Library' ? 'badge-purple' : 'badge-green'}">${p.type === 'Library' ? 'Library' : 'My Project'}</span></td>
            <td>${p.assetCount}</td>
            <td>${p.externalActorCount.toLocaleString()}</td>
            <td>${p.verseFileCount}</td>
            <td style="white-space:nowrap">
              ${p.id !== data.activeProjectId ? `<button class="btn" style="font-size:11px" onclick="activateProject('${p.id}')">Activate</button>` : ''}
              <button class="btn btn-danger" style="font-size:11px" onclick="removeProject('${p.id}','${esc(p.name)}')">Remove</button>
            </td>
          </tr>`).join('')}
        </tbody>
      </table></div>
    ` : '<div class="empty" style="margin-bottom:24px">No projects added yet. Scan a directory or add one manually below.</div>'}

    <h3 style="margin-bottom:12px">Scan for Projects</h3>
    <div class="card" style="margin-bottom:24px">
      <div style="margin-bottom:8px;font-size:12px;color:var(--text-secondary)">Point at a folder and we'll find all UEFN projects inside it</div>
      <div class="search-bar" style="margin-bottom:8px">
        <input type="text" id="scan-path" placeholder="Paste path or click Browse">
        <button class="btn" id="scan-browse">Browse</button>
        <button class="btn btn-primary" id="scan-btn">Scan</button>
      </div>
      <div id="scan-results"></div>
    </div>

    <h3 style="margin-bottom:12px">Add Manually</h3>
    <div class="card">
      <div style="margin-bottom:8px;font-size:12px;color:var(--text-secondary)">Paste the path to a specific UEFN project (the folder containing the .uefnproject file)</div>
      <div class="search-bar" style="margin-bottom:12px">
        <input type="text" id="add-path" placeholder="Paste path or click Browse">
        <button class="btn" id="add-browse">Browse</button>
      </div>
      <div style="display:flex;gap:16px;margin-bottom:12px">
        <label style="cursor:pointer;display:flex;align-items:center;gap:6px;font-size:13px">
          <input type="radio" name="project-type" value="MyProject" checked> <strong>My Project</strong>
        </label>
        <label style="cursor:pointer;display:flex;align-items:center;gap:6px;font-size:13px">
          <input type="radio" name="project-type" value="Library"> <strong>Library</strong>
        </label>
      </div>
      <div id="type-desc" style="font-size:12px;color:var(--text-secondary);margin-bottom:12px;padding:8px;background:var(--bg-tertiary);border-radius:6px">${esc(data.typeDescriptions.myProject)}</div>
      <button class="btn btn-primary" id="add-btn">Add Project</button>
    </div>
  `;

  // Type description toggle
  $$('input[name="project-type"]').forEach(r => r.addEventListener('change', () => {
    $('#type-desc').textContent = r.value === 'Library' ? data.typeDescriptions.library : data.typeDescriptions.myProject;
  }));

  // Browse buttons — open native Windows folder picker
  $('#scan-browse').addEventListener('click', () => pickFolder('scan-path'));
  $('#add-browse').addEventListener('click', () => pickFolder('add-path'));

  // Add project
  $('#add-btn').addEventListener('click', async () => {
    const path = $('#add-path').value.trim();
    if (!path) return;
    const type = $('input[name="project-type"]:checked').value;
    try {
      await apiPost('/projects/add', { path, type });
      await refreshSidebar();
      renderProjects();
    } catch (err) { alert('Error: ' + err.message); }
  });

  // Scan
  $('#scan-btn').addEventListener('click', async () => {
    const path = $('#scan-path').value.trim();
    if (!path) return;
    $('#scan-results').innerHTML = '<div class="loading"><div class="spinner"></div> Scanning...</div>';
    try {
      const found = await api(`/projects/scan?path=${encodeURIComponent(path)}`);
      if (found.length === 0) { $('#scan-results').innerHTML = '<div class="empty">No projects found</div>'; return; }
      $('#scan-results').innerHTML = `
        <div style="margin:8px 0;font-size:12px;color:var(--text-secondary)">${found.length} project(s) found</div>
        <div class="table-wrapper"><table><thead><tr><th>Project</th><th>Assets</th><th>Devices</th><th>Verse</th><th></th></tr></thead><tbody>
          ${found.map(p => `<tr>
            <td><strong>${esc(p.projectName)}</strong> ${p.isUefnProject ? '<span class="badge badge-blue">UEFN</span>' : ''}
              <div style="font-size:10px;color:var(--text-muted)">${esc(p.projectPath)}</div></td>
            <td>${p.assetCount}</td><td>${p.externalActorCount.toLocaleString()}</td><td>${p.verseFileCount}</td>
            <td>${p.alreadyAdded ? '<span class="badge badge-dim">Added</span>' :
              `<button class="btn" style="font-size:11px" onclick="addScanned('${esc(p.projectPath).replace(/'/g,"\\'")}','${esc(p.projectName).replace(/'/g,"\\'")}')">Add as Library</button>`}</td>
          </tr>`).join('')}
        </tbody></table></div>
      `;
    } catch (err) { $('#scan-results').innerHTML = `<div class="empty">Error: ${esc(err.message)}</div>`; }
  });
}

async function pickFolder(inputId) {
  try {
    const res = await api('/browse/pick-folder');
    if (!res.cancelled && res.path) {
      $('#' + inputId).value = res.path;
    }
  } catch (err) { alert('Could not open folder picker: ' + err.message); }
}

window.activateProject = async (id) => { await apiPost('/projects/activate', { id }); await refreshSidebar(); navigate(); };
window.removeProject = async (id, name) => { if (!confirm(`Remove "${name}" from project list?`)) return; await apiPost('/projects/remove', { id }); await refreshSidebar(); renderProjects(); };
window.addScanned = async (path, name) => { try { await apiPost('/projects/add', { path, type: 'Library', name }); await refreshSidebar(); renderProjects(); } catch(e) { alert(e.message); } };

// ========= Dashboard =========
async function renderDashboard() {
  const s = statusData || await api('/status');
  if (!s.id) { location.hash = '#/projects'; return; }

  let levels = []; try { levels = await api('/levels'); } catch {}
  const mc = { ReadOnly: 'red', Staged: 'yellow', Direct: 'green' };

  content().innerHTML = `
    <div class="page-header"><h2>${esc(s.projectName)}</h2><div class="subtitle">${esc(s.projectPath)}</div></div>
    <div class="card-grid">
      <div class="card"><div class="card-label">Total Assets</div><div class="card-value accent">${s.assetCount?.toLocaleString()}</div></div>
      <div class="card"><div class="card-label">Definitions</div><div class="card-value purple">${s.definitionCount || 0}</div></div>
      <div class="card"><div class="card-label">Levels</div><div class="card-value">${levels.length}</div></div>
      <div class="card"><div class="card-label">Verse Files</div><div class="card-value green">${s.verseCount}</div></div>
      <div class="card"><div class="card-label">Mode</div><div class="card-value ${mc[s.mode]||''}">${s.mode}</div></div>
      <div class="card"><div class="card-label">Type</div><div class="card-value">${s.type === 'Library' ? 'Library' : 'My Project'}</div></div>
    </div>
    <h3 style="margin:24px 0 12px">Levels</h3>
    ${levels.length > 0 ? `
      <div class="table-wrapper"><table><thead><tr><th>Level</th><th></th></tr></thead><tbody>
        ${levels.map(l => `<tr>
          <td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a><div style="font-size:11px;color:var(--text-muted)">${esc(l.relativePath)}</div></td>
          <td><a href="#/level?path=${encodeURIComponent(l.filePath)}&tab=devices" class="btn" style="font-size:11px">Devices</a></td>
        </tr>`).join('')}
      </tbody></table></div>` : '<div class="empty">No levels found in this project</div>'}
  `;
}

// ========= Levels =========
async function renderLevels() {
  const projects = (await api('/projects')).projects;
  let allLevels = [];
  for (const p of projects) {
    try {
      const levels = await api(`/levels?projectId=${p.id}`);
      levels.forEach(l => { l.projectName = p.name; l.projectType = p.type; });
      allLevels.push(...levels);
    } catch {}
  }

  content().innerHTML = `
    <div class="page-header"><h2>All Levels</h2><div class="subtitle">${allLevels.length} level(s) across ${projects.length} project(s)</div></div>
    <div class="table-wrapper"><table><thead><tr><th>Level</th><th>Project</th><th></th></tr></thead><tbody>
      ${allLevels.map(l => `<tr>
        <td><a href="#/level?path=${encodeURIComponent(l.filePath)}">${esc(l.name)}</a></td>
        <td><span class="badge ${l.projectType==='Library'?'badge-purple':'badge-green'}">${esc(l.projectName)}</span></td>
        <td><a href="#/level?path=${encodeURIComponent(l.filePath)}&tab=devices" class="btn" style="font-size:11px">Devices</a></td>
      </tr>`).join('')}
    </tbody></table></div>
  `;
}

// ========= Level Detail =========
async function renderLevelDetail(params) {
  const path = params.get('path');
  if (!path) return content().innerHTML = '<div class="empty">No level specified</div>';
  const name = path.split(/[\\/]/).pop().replace(/\.\w+$/, '');
  const tab = params.get('tab') || 'devices';

  content().innerHTML = `
    <div class="page-header"><h2>${esc(name)}</h2><div class="subtitle">${esc(path)}</div></div>
    <div class="tabs">
      <button class="tab ${tab==='devices'?'active':''}" data-tab="devices">Devices</button>
      <button class="tab ${tab==='actors'?'active':''}" data-tab="actors">All Actors</button>
      <button class="tab ${tab==='audit'?'active':''}" data-tab="audit">Audit</button>
    </div>
    <div id="tab-content"><div class="loading"><div class="spinner"></div> Scanning...</div></div>`;

  $$('.tab').forEach(t => t.addEventListener('click', () => { $$('.tab').forEach(x => x.classList.remove('active')); t.classList.add('active'); loadTab(t.dataset.tab, path); }));
  loadTab(tab, path);
}

let _cache = {};
async function loadTab(tab, path) {
  const tc = $('#tab-content');
  tc.innerHTML = '<div class="loading"><div class="spinner"></div> Loading...</div>';
  try {
    if (tab === 'devices') await renderDevicesTab(tc, path);
    else if (tab === 'actors') await renderActorsTab(tc, path);
    else if (tab === 'audit') { const r = await api(`/audit/level?path=${encodeURIComponent(path)}`); renderAuditFindings(tc, r); }
  } catch (err) { tc.innerHTML = `<div class="empty">Error: ${esc(err.message)}</div>`; }
}

async function renderDevicesTab(c, path) {
  if (!_cache[path]) _cache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
  const d = _cache[path];
  if (d.deviceCount === 0) { c.innerHTML = `<div class="empty">No devices found. ${d.staticActorCount} static actors.</div>`; return; }

  const groups = {};
  d.devices.forEach(dv => (groups[dv.className] = groups[dv.className] || []).push(dv));
  const sorted = Object.entries(groups).sort((a,b) => b[1].length - a[1].length);

  let h = `
    <div class="card-grid" style="margin-bottom:16px">
      <div class="card"><div class="card-label">Devices</div><div class="card-value accent">${d.deviceCount}</div></div>
      <div class="card"><div class="card-label">Types</div><div class="card-value purple">${sorted.length}</div></div>
      <div class="card"><div class="card-label">Static</div><div class="card-value">${d.staticActorCount}</div></div>
    </div>
    <div class="search-bar"><input type="text" id="dev-search" placeholder="Search devices..."></div><div id="dev-list">`;

  sorted.forEach(([cls, devs]) => {
    h += `<div class="device-group"><div class="device-group-header" onclick="this.parentElement.classList.toggle('collapsed')">
      <span class="tree-toggle">&#9662;</span><span class="badge badge-blue">${esc(devs[0].displayName)}</span><span class="tree-count">${devs.length}</span></div>
      <div class="device-group-items">`;
    devs.forEach((dv, i) => {
      const editable = dv.properties.filter(p => p.isEditable);
      h += `<div class="device-card" data-search="${esc((dv.displayName+' '+dv.className).toLowerCase())}">
        <div class="device-card-header"><div><strong>${esc(dv.displayName)}${devs.length>1?' #'+(i+1):''}</strong>
          <span style="color:var(--text-muted);font-size:11px;margin-left:8px">${dv.totalPropertyCount} props</span></div>
          <a href="#/device?path=${encodeURIComponent(dv.filePath)}" class="btn" style="font-size:11px;padding:3px 10px">Inspect</a></div>
        ${dv.hasPosition ? `<div style="font-size:11px;color:var(--text-secondary);margin-top:4px">Pos: (${dv.x.toFixed(0)}, ${dv.y.toFixed(0)}, ${dv.z.toFixed(0)})</div>` : ''}
        ${editable.length > 0 ? `<div class="device-props-preview">${editable.slice(0,4).map(p =>
          `<div class="prop-row"><span class="prop-name">${esc(p.name)}</span><span class="prop-value editable">${esc(truncate(p.value,50))}</span><span class="prop-type">${esc(p.type)}</span></div>`
        ).join('')}${editable.length>4?`<div style="font-size:11px;color:var(--text-muted)">+${editable.length-4} more</div>`:''}</div>` : ''}
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
  if (!_cache[path]) _cache[path] = await api(`/levels/devices-full?path=${encodeURIComponent(path)}`);
  const d = _cache[path];
  const bd = d.staticActorBreakdown || [];
  const max = bd[0]?.count || 1;
  const pal = ['#58a6ff','#3fb950','#d29922','#f85149','#bc8cff','#39d2c0','#d18616','#f778ba','#79c0ff','#7ee787'];

  c.innerHTML = `<div class="card-grid" style="margin-bottom:16px">
    <div class="card"><div class="card-label">Static Actors</div><div class="card-value">${d.staticActorCount}</div></div>
    <div class="card"><div class="card-label">Class Types</div><div class="card-value purple">${bd.length}</div></div></div>
    <h3 style="margin-bottom:12px">Class Breakdown</h3>
    <div style="display:flex;flex-direction:column;gap:4px">${bd.map((cls,i) => {
      const pct = (cls.count/max*100).toFixed(0);
      return `<div style="display:flex;align-items:center;gap:12px">
        <div style="width:220px;font-size:12px;text-align:right;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(cls.displayName)}</div>
        <div style="flex:1;height:18px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden"><div style="width:${pct}%;height:100%;background:${pal[i%pal.length]};border-radius:3px;min-width:2px"></div></div>
        <div style="width:40px;font-size:12px;font-weight:600">${cls.count}</div></div>`;
    }).join('')}</div>`;
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
    <div class="card-grid" style="margin-bottom:24px">
      <div class="card"><div class="card-label">Class</div><div style="font-size:13px;color:var(--accent)">${esc(dv.className)}</div></div>
      ${dv.hasPosition ? `<div class="card"><div class="card-label">Position</div><div style="font-size:13px">(${dv.x.toFixed(1)}, ${dv.y.toFixed(1)}, ${dv.z.toFixed(1)})</div></div>
      <div class="card"><div class="card-label">Rotation</div><div style="font-size:13px">${dv.rotationYaw.toFixed(1)}&deg;</div></div>` : ''}
      <div class="card"><div class="card-label">Components</div><div class="card-value purple">${dv.components.length}</div></div>
      <div class="card"><div class="card-label">Editable</div><div class="card-value green">${ed.length}</div></div>
    </div>
    ${ed.length > 0 ? `<h3 style="margin-bottom:12px">Configurable Properties</h3>
      <div class="table-wrapper"><table><thead><tr><th>Property</th><th>Value</th><th>Type</th></tr></thead><tbody>
        ${ed.map(p => `<tr><td><span class="prop-name">${esc(p.name)}</span></td><td class="prop-value">${esc(p.value)}</td><td><span class="badge badge-dim">${esc(p.type)}</span></td></tr>`).join('')}
      </tbody></table></div>` : ''}
    ${other.length > 0 ? `<details style="margin-top:16px"><summary style="cursor:pointer;color:var(--text-secondary);font-size:13px">Other Properties (${other.length})</summary>
      <div class="table-wrapper" style="margin-top:8px"><table><thead><tr><th>Property</th><th>Value</th><th>Type</th></tr></thead><tbody>
        ${other.map(p => `<tr><td style="color:var(--text-secondary)">${esc(p.name)}</td><td style="color:var(--text-muted);font-size:12px">${esc(truncate(p.value,120))}</td><td><span class="badge badge-dim">${esc(p.type)}</span></td></tr>`).join('')}
      </tbody></table></div></details>` : ''}
    <details style="margin-top:16px"><summary style="cursor:pointer;color:var(--text-secondary);font-size:13px">Components (${dv.components.length})</summary>
      <div class="table-wrapper" style="margin-top:8px"><table><thead><tr><th>Component</th><th>Class</th><th>Props</th></tr></thead><tbody>
        ${dv.components.map(c => `<tr><td>${esc(c.objectName)}</td><td><span class="badge badge-purple">${esc(c.className)}</span></td><td>${c.propertyCount}</td></tr>`).join('')}
      </tbody></table></div></details>
    <div style="margin-top:16px;font-size:11px;color:var(--text-muted)">${esc(path)}</div>`;
}

// ========= Assets (definitions only) =========
let _assetCache = null;
async function renderAssets() {
  content().innerHTML = `<div class="page-header"><h2>Asset Definitions</h2><div class="subtitle">Blueprints, materials, and custom props (excludes placed instances)</div></div>
    <div class="search-bar"><input type="text" id="asset-search" placeholder="Search definitions..."></div>
    <div id="asset-list"><div class="loading"><div class="spinner"></div> Loading...</div></div>`;

  if (!_assetCache) _assetCache = await api('/assets');

  function render(filtered) {
    if (filtered.length === 0) { $('#asset-list').innerHTML = '<div class="empty">No asset definitions found</div>'; return; }
    $('#asset-list').innerHTML = `<div style="margin-bottom:8px;font-size:12px;color:var(--text-muted)">${filtered.length} definition(s)</div>
      <div class="table-wrapper"><table><thead><tr><th>Name</th><th>Size</th><th>Modified</th></tr></thead><tbody>
        ${filtered.slice(0, 200).map(a => `<tr>
          <td><a href="#/asset?path=${encodeURIComponent(a.filePath)}">${esc(a.name)}</a><div style="font-size:11px;color:var(--text-muted)">${esc(a.relativePath)}</div></td>
          <td>${fileSize(a.fileSize)}</td>
          <td style="font-size:12px;color:var(--text-secondary)">${new Date(a.lastModified).toLocaleDateString()}</td>
        </tr>`).join('')}
      </tbody></table></div>`;
  }

  function filter() {
    const q = $('#asset-search').value.toLowerCase();
    render(_assetCache.filter(a => !q || a.name.toLowerCase().includes(q) || a.relativePath.toLowerCase().includes(q)));
  }
  $('#asset-search').addEventListener('input', filter);
  filter();
}

async function renderAssetDetail(params) {
  const path = params.get('path');
  if (!path) return;
  const d = await api(`/assets/inspect?path=${encodeURIComponent(path)}`);

  content().innerHTML = `
    <div class="page-header"><h2>${esc(d.name)}</h2><div class="subtitle">${esc(d.relativePath)}</div></div>
    <div class="card-grid">
      <div class="card"><div class="card-label">Class</div><div style="font-size:13px;color:var(--accent)">${esc(d.assetClass)}</div></div>
      <div class="card"><div class="card-label">Size</div><div style="font-size:13px">${fileSize(d.fileSize)}</div></div>
      <div class="card"><div class="card-label">Exports</div><div class="card-value purple">${d.exportCount}</div></div>
      <div class="card"><div class="card-label">Imports</div><div class="card-value">${d.importCount}</div></div>
    </div>
    <h3 style="margin:24px 0 12px">Exports</h3>
    ${(d.exports||[]).map(e => `
      <details class="card" style="margin-bottom:8px"><summary style="cursor:pointer;display:flex;justify-content:space-between;align-items:center">
        <span><span class="badge badge-purple">${esc(e.className)}</span> ${esc(e.objectName)}</span>
        <span style="font-size:11px;color:var(--text-muted)">${e.properties?.length || 0} props &middot; ${fileSize(e.serialSize)}</span>
      </summary>
      ${(e.properties?.length > 0) ? `<div class="prop-list" style="margin-top:8px;padding-top:8px;border-top:1px solid var(--border)">
        ${e.properties.map(p => `<div class="prop-name">${esc(p.name)} <span class="prop-type">${esc(p.type)}</span></div><div class="prop-value">${esc(truncate(p.value,120))}</div>`).join('')}
      </div>` : '<div style="margin-top:8px;font-size:12px;color:var(--text-muted)">No properties</div>'}
      </details>
    `).join('')}
  `;
}

// ========= Audit =========
async function renderAudit() {
  content().innerHTML = '<div class="loading"><div class="spinner"></div> Running audit...</div>';
  const r = await api('/audit');
  content().innerHTML = `<div class="page-header"><h2>Project Audit</h2></div><div id="ac"></div>`;
  renderAuditFindings($('#ac'), r);
}

function renderAuditFindings(c, r) {
  const ic = { Error: {i:'&#10060;',c:'red'}, Warning: {i:'&#9888;',c:'yellow'}, Info: {i:'&#8505;',c:'accent'} };
  const bc = { Pass: 'badge-green', Warning: 'badge-yellow', Fail: 'badge-red' };
  const f = r.findings || [];
  c.innerHTML = `<div style="margin-bottom:16px"><span class="badge ${bc[r.status]||'badge-dim'}">${r.status}</span> <span style="color:var(--text-secondary);font-size:13px">${f.length} finding(s)</span></div>
    ${f.length===0?'<div class="empty">No issues</div>':f.map(fi => {
      const s = ic[fi.severity]||ic.Info;
      return `<div class="finding"><div class="finding-icon" style="color:var(--${s.c})">${s.i}</div><div class="finding-body">
        <div class="finding-category">${esc(fi.category)} &middot; ${esc(fi.location||'')}</div>
        <div class="finding-message">${esc(fi.message)}</div>
        ${fi.suggestion?`<div class="finding-suggestion">${esc(fi.suggestion)}</div>`:''}</div></div>`;
    }).join('')}`;
}

// ========= Staged =========
async function renderStaged() {
  const s = await api('/staged');
  content().innerHTML = `<div class="page-header"><h2>Staged Changes</h2><div class="subtitle">${s.length} file(s)</div></div>
    ${s.length===0?'<div class="empty">No staged modifications.</div>':`
      <div style="margin-bottom:16px;display:flex;gap:8px"><button class="btn btn-primary" onclick="applyStaged()">Apply All</button><button class="btn btn-danger" onclick="discardStaged()">Discard All</button></div>
      <div class="table-wrapper"><table><thead><tr><th>File</th><th>Type</th><th>Size</th><th>Staged</th></tr></thead><tbody>
        ${s.map(f => `<tr><td>${esc(f.relativePath)}</td><td><span class="badge ${f.exists?'badge-yellow':'badge-green'}">${f.exists?'Update':'New'}</span></td><td>${fileSize(f.size)}</td><td style="font-size:12px;color:var(--text-secondary)">${new Date(f.stagedAt).toLocaleString()}</td></tr>`).join('')}
      </tbody></table></div>`}`;
}
window.applyStaged = async () => { if(!confirm('Apply all?'))return; await apiPost('/staged/apply'); renderStaged(); };
window.discardStaged = async () => { if(!confirm('Discard all?'))return; await apiPost('/staged/discard'); renderStaged(); };
