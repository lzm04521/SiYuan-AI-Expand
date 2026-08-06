'use strict';

// ============== 工具 ==============
const MASK = '********';
const $ = (id) => document.getElementById(id);

async function fetchJson(url, opts = {}) {
  const headers = opts.headers || {};
  if (opts.body !== undefined && typeof opts.body !== 'string') {
    headers['Content-Type'] = 'application/json';
    opts.body = JSON.stringify(opts.body);
  } else if (opts.body !== undefined) {
    headers['Content-Type'] = headers['Content-Type'] || 'application/json';
  }
  opts.headers = headers;
  // 同源请求浏览器自动带 Origin，CSRF 中间件据此放行
  const res = await fetch(url, opts);
  let data = null;
  const txt = await res.text();
  if (txt) {
    try { data = JSON.parse(txt); } catch { data = txt; }
  }
  return { ok: res.ok, status: res.status, data };
}

function fmtDate(s) {
  if (!s) return '';
  try {
    const d = new Date(s);
    return d.toLocaleString();
  } catch { return s; }
}

function setText(id, txt, cls) {
  const el = $(id);
  if (!el) return;
  el.textContent = txt ?? '';
  if (cls !== undefined) el.className = cls;
}

// ============== 配置：思源连接 + 同步设置 ==============
async function loadConfig() {
  const { ok, data } = await fetchJson('/api/config');
  if (!ok) return;
  $('fServerUrl').value = data.siyuan?.serverUrl ?? '';
  // token 脱敏：服务端返回 ******** 占位；表单显示空，避免误导用户编辑
  $('fToken').value = '';
  $('fToken').placeholder = data.siyuan?.hasToken ? MASK : '未设置（留空）';
  $('fDefaultNotebook').value = data.siyuan?.defaultNotebook ?? '';
  $('fInterval').value = data.sync?.intervalMinutes ?? '';
  $('fRunOnStart').checked = !!data.sync?.runOnStart;
}

async function saveConfig() {
  const body = {
    serverUrl: $('fServerUrl').value.trim() || null,
    defaultNotebook: $('fDefaultNotebook').value.trim() || null,
    intervalMinutes: Number($('fInterval').value) || null,
    runOnStart: $('fRunOnStart').checked,
  };
  // 用户没改 token 字段：留空，服务端会保留原值（PreserveOriginalIfMasked/Empty）
  const tokenInput = $('fToken').value;
  body.token = tokenInput ? tokenInput : null;

  const { ok, data } = await fetchJson('/api/config', { method: 'PUT', body });
  if (ok) {
    setText('siyuanTestResult', '配置已保存', 'muted');
    await loadConfig();
  } else {
    setText('siyuanTestResult', `保存失败：${data?.message || data}`, 'err');
  }
}

async function testSiyuan() {
  setText('siyuanTestResult', '测试连接中…', 'muted');
  const { ok, data } = await fetchJson('/api/siyuan/test', { method: 'POST' });
  if (ok && data.ok) {
    const list = (data.notebooks || []).join('，') || '（无笔记本）';
    setText('siyuanTestResult', `连接成功，笔记本：${list}`, 'muted');
  } else {
    setText('siyuanTestResult', `连接失败：${data?.message || data?.details || '未知错误'}`, 'err');
  }
}

// ============== 项目列表 ==============
let _projects = [];

async function loadProjects() {
  const { ok, data } = await fetchJson('/api/projects');
  if (!ok) return;
  _projects = Array.isArray(data) ? data : [];
  renderProjects();
}

function renderProjects() {
  const tbody = $('projectsTable').querySelector('tbody');
  tbody.innerHTML = '';
  if (_projects.length === 0) {
    tbody.innerHTML = '<tr><td colspan="8" class="muted">暂无项目，点击"新增项目"</td></tr>';
    return;
  }
  for (const p of _projects) {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escapeHtml(p.name)}</td>
      <td>${p.enabled ? '是' : '否'}</td>
      <td><code>${escapeHtml(p.docPath)}</code></td>
      <td>${escapeHtml(p.notebook || '')}</td>
      <td>${escapeHtml(p.parentPath || '')}</td>
      <td><button type="button" data-act="edit">编辑</button></td>
      <td><button type="button" data-act="init" class="secondary">同步创建父目录</button></td>
      <td><button type="button" data-act="del" class="danger">删除</button></td>
    `;
    tr.querySelector('[data-act="edit"]').addEventListener('click', () => openProjectDialog(p));
    tr.querySelector('[data-act="init"]').addEventListener('click', () => initParent(p.name));
    tr.querySelector('[data-act="del"]').addEventListener('click', () => deleteProject(p.name));
    tbody.appendChild(tr);
  }
}

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function openProjectDialog(existing) {
  const dlg = $('projectDialog');
  if (existing) {
    $('projectDialogTitle').textContent = `编辑项目：${existing.name}`;
    $('pName').value = existing.name;
    $('pName').readOnly = true;
    $('pDocPath').value = existing.docPath || '';
    $('pNotebook').value = existing.notebook || '';
    $('pParentPath').value = existing.parentPath || '';
    $('pEnabled').checked = existing.enabled !== false;
  } else {
    $('projectDialogTitle').textContent = '新增项目';
    $('pName').value = '';
    $('pName').readOnly = false;
    $('pDocPath').value = '';
    $('pNotebook').value = '';
    $('pParentPath').value = '';
    $('pEnabled').checked = true;
  }
  dlg.showModal();
}

async function saveProjectFromDialog() {
  const isEdit = $('pName').readOnly;
  const body = {
    name: $('pName').value.trim(),
    docPath: $('pDocPath').value.trim(),
    notebook: $('pNotebook').value.trim(),
    parentPath: $('pParentPath').value.trim(),
    enabled: $('pEnabled').checked,
  };
  if (!body.name || !body.docPath) {
    alert('名称与 docPath 必填');
    return;
  }
  const url = isEdit ? `/api/projects/${encodeURIComponent(body.name)}` : '/api/projects';
  const method = isEdit ? 'PUT' : 'POST';
  const { ok, data } = await fetchJson(url, { method, body });
  if (ok) {
    $('projectDialog').close();
    await loadProjects();
  } else {
    alert(`保存失败：${data?.message || data}`);
  }
}

async function deleteProject(name) {
  if (!confirm(`确认删除项目 '${name}'？状态库的文件哈希不会被清。`)) return;
  const { ok, data } = await fetchJson(`/api/projects/${encodeURIComponent(name)}`, { method: 'DELETE' });
  if (ok) await loadProjects();
  else alert(`删除失败：${data?.message || data}`);
}

async function initParent(name) {
  if (!confirm(`将按 parentPath 在思源中逐级创建缺失的父文档（已存在则跳过）。继续？`)) return;
  const { ok, data } = await fetchJson(`/api/projects/${encodeURIComponent(name)}/init-parent`, { method: 'POST' });
  if (ok) {
    alert(data.created ? `已创建（docId=${data.docId || ''}）` : `已存在或无需创建：${data.message || ''}`);
  } else {
    alert(`创建失败：${data?.message || data}`);
  }
}

// ============== 立即同步 ==============
async function runSync() {
  $('btnRunSync').disabled = true;
  setText('syncRunResult', '请求中…', 'muted');
  const { ok, status, data } = await fetchJson('/api/sync/run', { method: 'POST' });
  $('btnRunSync').disabled = false;
  if (ok) {
    setText('syncRunResult', '已启动同步（后台运行中，几秒后点[刷新状态]）', 'muted');
    setBadge(true);
  } else if (status === 409) {
    setText('syncRunResult', '同步已在进行中，请稍后刷新状态', 'err');
    setBadge(true);
  } else {
    setText('syncRunResult', `启动失败：${data?.message || data}`, 'err');
  }
}

function setBadge(running) {
  const el = $('syncBadge');
  el.textContent = running ? '运行中' : '空闲';
  el.className = 'badge ' + (running ? 'badge-running' : 'badge-idle');
}

// ============== 状态 ==============
async function loadStatus() {
  const { ok, data } = await fetchJson('/api/status');
  if (!ok) {
    setText('statusEmpty', `加载失败：${data?.message || data}`, 'err');
    return;
  }
  const projects = data.projects || [];
  const details = data.details || [];
  const tbody = $('statusTable').querySelector('tbody');
  tbody.innerHTML = '';

  if (projects.length === 0) {
    setText('statusEmpty', '暂无同步记录', 'muted');
    $('statusEmpty').hidden = false;
    return;
  }
  $('statusEmpty').hidden = true;

  // 按项目分组明细
  const detailsByProject = new Map();
  for (const d of details) {
    if (!detailsByProject.has(d.project)) detailsByProject.set(d.project, []);
    detailsByProject.get(d.project).push(d);
  }

  for (const p of projects) {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escapeHtml(p.project)}</td>
      <td>${escapeHtml(fmtDate(p.startedAt))}</td>
      <td>${p.success}</td>
      <td>${p.skipped}</td>
      <td>${p.failed}</td>
      <td><span class="status-${escapeHtml(p.status)}">${escapeHtml(p.status)}</span></td>
      <td>${escapeHtml(p.error || '')}</td>
      <td><button type="button" class="secondary" data-act="toggle">展开</button></td>
    `;
    const toggleBtn = tr.querySelector('[data-act="toggle"]');
    toggleBtn.disabled = !(detailsByProject.get(p.project)?.length);
    toggleBtn.addEventListener('click', () => toggleDetails(tr, p.project, detailsByProject));
    tbody.appendChild(tr);
  }
}

function toggleDetails(tr, projectName, detailsByProject) {
  const existing = tr.nextSibling;
  if (existing && existing.classList && existing.classList.contains('sub-row')) {
    existing.remove();
    return;
  }
  const files = detailsByProject.get(projectName) || [];
  const subRow = document.createElement('tr');
  subRow.classList.add('sub-row');
  subRow.innerHTML = `<td colspan="8">
    <table class="sub-table">
      <thead><tr><th>相对路径</th><th>结果</th><th>错误</th></tr></thead>
      <tbody>
        ${files.map(f => `<tr class="row-${escapeHtml(f.outcome)}">
          <td><code>${escapeHtml(f.relPath)}</code></td>
          <td>${escapeHtml(f.outcome)}</td>
          <td>${escapeHtml(f.error || '')}</td>
        </tr>`).join('')}
      </tbody>
    </table>
  </td>`;
  tr.parentNode.insertBefore(subRow, tr.nextSibling);
}

// ============== 事件绑定 + 启动 ==============
function bindEvents() {
  $('btnSaveConfig').addEventListener('click', saveConfig);
  $('btnTestSiyuan').addEventListener('click', testSiyuan);
  $('btnAddProject').addEventListener('click', () => openProjectDialog(null));
  $('btnReloadProjects').addEventListener('click', loadProjects);
  $('btnRunSync').addEventListener('click', runSync);
  $('btnRefreshStatus').addEventListener('click', loadStatus);
  $('pCancel').addEventListener('click', () => $('projectDialog').close());
  $('pSave').addEventListener('click', saveProjectFromDialog);
}

async function init() {
  bindEvents();
  await Promise.all([loadConfig(), loadProjects(), loadStatus()]);
}

document.addEventListener('DOMContentLoaded', init);
