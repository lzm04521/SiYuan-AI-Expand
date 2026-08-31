'use strict';

/* SiYuan-AI-Expand Web 管理页（Vue 3 自托管，无构建链）
 * 后端契约（与旧原生版一致，字段名不变）：
 *   /api/config GET → { siyuan:{serverUrl,hasToken,defaultNotebook,exePath,autoStartOnSync},
 *                        sync:{intervalMinutes,runOnStart}, mcp:{enabled}, web:{port} }
 *   /api/config PUT ← { serverUrl, defaultNotebook, siyuanExePath, autoStartOnSync,
 *                        intervalMinutes, runOnStart, token } 或 { mcpEnabled }
 *   /api/projects GET/POST；PUT/DELETE /api/projects/{name}；POST /api/projects/{name}/init-parent
 *   /api/siyuan/test POST → { ok, notebooks[], message, details }
 *   /api/sync/run POST（409=已在同步中）；/api/status GET → { runId, projects[], details[] }
 *   /api/sync/history?project&from&to&limit&offset → { runs:[{runId, projects[]}], hasMore }
 *   /api/sync/history/{runId}/details → { details:[{project, relPath, outcome, error}] }
 *   /api/system/info GET → { version, repoUrl, uptimeSeconds, workingSetBytes }
 *   /api/system/autostart GET/PUT；/api/system/update/check|apply POST
 */

const MASK = '********';
const LOG_PAGE = 20;
// 文件级结果中文标签；Success 为旧数据（未区分新建/更新）
const OUTCOME_LABEL = { Created: '新建', Updated: '更新', Skipped: '跳过', Failed: '失败', Success: '成功', Deleted: '删除' };
const SORT_OPTIONS = [
  { value: null, label: '不调整' },
  { value: 3, label: '更新时间降序' }, { value: 2, label: '更新时间升序' },
  { value: 10, label: '创建时间降序' }, { value: 9, label: '创建时间升序' },
  { value: 5, label: '文件名降序' }, { value: 4, label: '文件名升序' },
  { value: 6, label: '自定义' },
];

// fetch 封装：非 2xx 抛错并带 status；错误体 {message} 透出后端校验文案；
// 同源请求浏览器自动带 Origin，CSRF 中间件据此放行。
const api = {
  async request(method, url, body) {
    const opt = { method };
    if (body !== undefined) {
      opt.headers = { 'Content-Type': 'application/json' };
      opt.body = JSON.stringify(body);
    }
    const res = await fetch(url, opt);
    const text = await res.text();
    let data = null;
    if (text) { try { data = JSON.parse(text); } catch { data = null; } }
    if (!res.ok) {
      const err = new Error((data && data.message) ? data.message : ('HTTP ' + res.status));
      err.status = res.status;
      throw err;
    }
    return data;
  },
  get: (u) => api.request('GET', u),
  post: (u, b) => api.request('POST', u, b ?? {}),
  put: (u, b) => api.request('PUT', u, b),
  del: (u) => api.request('DELETE', u),
};

function fmtDate(s) {
  if (!s) return '';
  try { return new Date(s).toLocaleString(); } catch { return s; }
}
function fmtBytes(n) {
  if (n == null) return '…';
  if (n < 1024 * 1024) return (n / 1024).toFixed(0) + ' KB';
  return (n / 1024 / 1024).toFixed(1) + ' MB';
}
function fmtUptime(sec) {
  if (sec == null) return '…';
  const h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60), s = Math.floor(sec % 60);
  if (h) return h + 'h ' + String(m).padStart(2, '0') + 'm';
  if (m) return m + 'm ' + String(s).padStart(2, '0') + 's';
  return s + 's';
}
function sortModeLabel(sm) {
  const hit = SORT_OPTIONS.find((o) => o.value === sm);
  return hit ? hit.label : '不调整';
}
function outcomeLabel(o) { return OUTCOME_LABEL[o] || o; }
function statusBadge(s) {
  if (s === 'Success') return { cls: 'badge badge-green', text: '成功' };
  if (s === 'Failed') return { cls: 'badge badge-red', text: '失败' };
  return { cls: 'badge badge-purple', text: s };
}

/* ======================= 概览页 ======================= */
const OverviewPage = {
  emits: ['running', 'idle'],
  data: () => ({ projects: [], status: null, runResult: '', runningReq: false, expanded: {} }),
  computed: {
    lastByProject() {
      const map = {};
      ((this.status && this.status.projects) || []).forEach((p) => { map[p.project] = p; });
      return map;
    },
    statOk() { return ((this.status && this.status.projects) || []).reduce((a, p) => a + (p.success || 0), 0); },
    statFail() { return ((this.status && this.status.projects) || []).reduce((a, p) => a + (p.failed || 0), 0); },
    statRate() {
      const ok = this.statOk, fail = this.statFail;
      return ok + fail === 0 ? '—' : (100 * ok / (ok + fail)).toFixed(1) + '%';
    },
  },
  mounted() { this.refresh(); },
  methods: {
    async refresh() {
      try {
        const [status, projects] = await Promise.all([api.get('/api/status'), api.get('/api/projects')]);
        this.status = status;
        this.projects = Array.isArray(projects) ? projects : [];
        // 最近一轮全部结束 → 徽章回空闲（启动同步后由用户点[刷新状态]触发）
        const ps = (status && status.projects) || [];
        if (ps.length === 0 || ps.every((p) => p.finishedAt)) this.$emit('idle');
      } catch (e) { alert('加载失败：' + e.message); }
    },
    async runSync() {
      this.runningReq = true;
      try {
        await api.post('/api/sync/run');
        this.runResult = '已启动同步（后台运行中，几秒后点[刷新状态]）';
        this.$emit('running');
      } catch (e) {
        if (e.status === 409) {
          this.runResult = '同步已在进行中，请稍后刷新状态';
          this.$emit('running');
        } else {
          this.runResult = '启动失败：' + e.message;
        }
      }
      this.runningReq = false;
    },
    roundBadge(p) {
      const lr = this.lastByProject[p.name];
      if (!lr) return { cls: 'badge badge-gray', text: '未运行', time: '—' };
      if (lr.failed > 0) {
        return { cls: 'badge badge-red', text: lr.failed + ' 失败' + (lr.success ? ' · ' + lr.success + ' 成功' : ''), time: fmtDate(lr.startedAt) };
      }
      return { cls: 'badge badge-green', text: lr.success + ' 成功' + (lr.skipped ? ' · ' + lr.skipped + ' 跳过' : '') + (lr.deleted ? ' · ' + lr.deleted + ' 删除' : ''), time: fmtDate(lr.startedAt) };
    },
    detailsFor(name) { return ((this.status && this.status.details) || []).filter((d) => d.project === name); },
    async toggleProject(p) {
      try {
        await api.put('/api/projects/' + encodeURIComponent(p.name), {
          name: p.name, docPath: p.docPath, notebook: p.notebook, parentPath: p.parentPath,
          sortMode: p.sortMode, enabled: p.enabled === false,
          settleMinutes: p.settleMinutes, includePattern: p.includePattern, excludePattern: p.excludePattern,
          deleteSync: p.deleteSync === true,
        });
        await this.refresh();
      } catch (e) { alert('操作失败：' + e.message); }
    },
  },
  template: `
  <div>
    <div class="stat-row">
      <div class="card stat"><div class="num">{{ projects.length }}</div><div class="label">项目</div></div>
      <div class="card stat"><div class="num">{{ projects.filter(p => p.enabled !== false).length }}</div><div class="label">启用中</div></div>
      <div class="card stat"><div class="num">{{ statOk }}</div><div class="label">最近一轮成功</div></div>
      <div class="card stat"><div class="num bad">{{ statFail }}</div><div class="label">最近一轮失败</div></div>
      <div class="card stat"><div class="num">{{ statRate }}</div><div class="label">最近一轮成功率</div></div>
    </div>

    <div class="toolbar">
      <button class="btn btn-primary" :disabled="runningReq" @click="runSync">立即全部同步</button>
      <button class="btn" @click="refresh">刷新状态</button>
      <span class="result" v-if="runResult">{{ runResult }}</span>
    </div>

    <div class="grid" v-if="projects.length">
      <div class="card proj-card" v-for="p in projects" :key="p.name">
        <h3>{{ p.name }} <span :class="p.enabled !== false ? 'badge badge-green' : 'badge badge-gray'">{{ p.enabled !== false ? '启用' : '停用' }}</span></h3>
        <div class="meta">{{ p.docPath }} · 笔记本：{{ p.notebook || '默认' }} · {{ p.parentPath || '/' }}</div>
        <div class="last-run"><span>最近一轮 {{ roundBadge(p).time }}</span><b :class="roundBadge(p).cls">{{ roundBadge(p).text }}</b></div>
        <div class="ops"><button class="btn" @click="toggleProject(p)">{{ p.enabled !== false ? '停用' : '启用' }}</button></div>
      </div>
    </div>
    <p class="card result-line" v-else>暂无项目，请到[项目]页新增。</p>

    <div class="card">
      <div class="sec-h">最近一轮状态</div>
      <table v-if="(status && status.projects || []).length">
        <thead><tr><th>项目</th><th>开始</th><th>成功</th><th>跳过</th><th>失败</th><th>已删</th><th>状态</th><th>错误</th><th>明细</th></tr></thead>
        <tbody>
          <template v-for="p in status.projects" :key="p.project">
            <tr>
              <td>{{ p.project }}</td>
              <td class="mono">{{ fmtDate(p.startedAt) }}</td>
              <td>{{ p.success }}</td>
              <td>{{ p.skipped }}</td>
              <td>{{ p.failed }}</td>
              <td>{{ p.deleted ?? 0 }}</td>
              <td><span :class="statusBadge(p.status).cls">{{ statusBadge(p.status).text }}</span></td>
              <td>{{ p.error || '' }}</td>
              <td><button class="btn-link" :disabled="!detailsFor(p.project).length" @click="expanded[p.project] = !expanded[p.project]">{{ expanded[p.project] ? '收起' : '展开' }}</button></td>
            </tr>
            <tr class="expanded-row" v-if="expanded[p.project]">
              <td colspan="9">
                <table class="sub-table">
                  <thead><tr><th>相对路径</th><th>结果</th><th>错误</th></tr></thead>
                  <tbody>
                    <tr v-for="f in detailsFor(p.project)" :key="f.relPath">
                      <td><code>{{ f.relPath }}</code></td>
                      <td><span :class="'outcome-' + f.outcome">{{ outcomeLabel(f.outcome) }}</span></td>
                      <td>{{ f.error || '' }}</td>
                    </tr>
                  </tbody>
                </table>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
      <p class="result-line" v-else>暂无同步记录</p>
    </div>
  </div>`,
};

/* ======================= 项目页 ======================= */
const ProjectsPage = {
  data: () => ({ projects: [], editing: null, sortOptions: SORT_OPTIONS }),
  mounted() { this.load(); },
  methods: {
    async load() {
      try {
        const list = await api.get('/api/projects');
        this.projects = Array.isArray(list) ? list : [];
      } catch (e) { alert('加载失败：' + e.message); }
    },
    openNew() {
      this.editing = { isNew: true, name: '', docPath: '', notebook: '', parentPath: '', sortMode: null, enabled: true,
        settleMinutes: '', includePattern: '', excludePattern: '', deleteSync: false };
    },
    openEdit(p) {
      this.editing = {
        isNew: false, name: p.name, docPath: p.docPath || '', notebook: p.notebook || '',
        parentPath: p.parentPath || '', sortMode: p.sortMode == null ? null : Number(p.sortMode),
        settleMinutes: p.settleMinutes == null ? '' : String(p.settleMinutes),
        includePattern: p.includePattern || '', excludePattern: p.excludePattern || '',
        deleteSync: p.deleteSync === true,
        enabled: p.enabled !== false,
      };
    },
    close() { this.editing = null; },
    async save() {
      const f = this.editing;
      if (!f.name.trim() || !f.docPath.trim()) { alert('名称与 docPath 必填'); return; }
      const body = {
        name: f.name.trim(), docPath: f.docPath.trim(), notebook: f.notebook.trim(),
        parentPath: f.parentPath.trim(),
        sortMode: (f.sortMode === null || f.sortMode === '' || f.sortMode === undefined) ? null : Number(f.sortMode),
        settleMinutes: (f.settleMinutes === '' || f.settleMinutes === null || f.settleMinutes === undefined) ? null : Number(f.settleMinutes),
        includePattern: (f.includePattern || '').trim(), excludePattern: (f.excludePattern || '').trim(),
        deleteSync: f.deleteSync === true,
        enabled: f.enabled,
      };
      try {
        if (f.isNew) await api.post('/api/projects', body);
        else await api.put('/api/projects/' + encodeURIComponent(body.name), body);
        this.editing = null;
        await this.load();
      } catch (e) { alert('保存失败：' + e.message); }
    },
    async remove(p) {
      if (!confirm(`确认删除项目 '${p.name}'？状态库的文件哈希不会被清。`)) return;
      try {
        await api.del('/api/projects/' + encodeURIComponent(p.name));
        await this.load();
      } catch (e) { alert('删除失败：' + e.message); }
    },
    async initParent(p) {
      if (!confirm('将按 parentPath 在思源中逐级创建缺失的父文档（已存在则跳过）。继续？')) return;
      try {
        const r = await api.post('/api/projects/' + encodeURIComponent(p.name) + '/init-parent');
        alert(r.created ? `已创建（docId=${r.docId || ''}）` : `已存在或无需创建：${r.message || ''}`);
      } catch (e) { alert('创建失败：' + e.message); }
    },
  },
  template: `
  <div>
    <div class="toolbar">
      <button class="btn btn-primary" @click="openNew">新增项目</button>
      <button class="btn" @click="load">刷新</button>
    </div>
    <div class="card">
      <table v-if="projects.length">
        <thead><tr><th>名称</th><th>启用</th><th>docPath</th><th>笔记本</th><th>父路径</th><th>排序</th><th style="width:230px">操作</th></tr></thead>
        <tbody>
          <tr v-for="p in projects" :key="p.name">
            <td>{{ p.name }}</td>
            <td><span :class="p.enabled !== false ? 'badge badge-green' : 'badge badge-gray'">{{ p.enabled !== false ? '是' : '否' }}</span></td>
            <td><code>{{ p.docPath }}</code></td>
            <td>{{ p.notebook || '' }}</td>
            <td>{{ p.parentPath || '' }}</td>
            <td>{{ sortModeLabel(p.sortMode) }}</td>
            <td>
              <button class="btn-link" @click="openEdit(p)">编辑</button>
              <button class="btn-link" @click="initParent(p)">同步创建父目录</button>
              <button class="btn-link danger" @click="remove(p)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <p class="result-line" v-else>暂无项目，点击"新增项目"。</p>
    </div>

    <div class="modal-mask" v-if="editing" @click.self="close">
      <div class="modal">
        <h3>{{ editing.isNew ? '新增项目' : ('编辑项目：' + editing.name) }}</h3>
        <div class="form-row"><label>名称</label><input type="text" v-model="editing.name" :readonly="!editing.isNew"></div>
        <div class="form-row"><label>docPath</label><input type="text" v-model="editing.docPath" placeholder="绝对或相对路径"></div>
        <div class="form-row"><label>笔记本</label><input type="text" v-model="editing.notebook" placeholder="留空走默认"></div>
        <div class="form-row"><label>父路径</label><input type="text" v-model="editing.parentPath" placeholder="如 /JPT"></div>
        <div class="form-row"><label>同步后排序</label>
          <select v-model="editing.sortMode">
            <option v-for="o in sortOptions" :key="o.label" :value="o.value">{{ o.label }}</option>
          </select>
          <span class="hint">需思源 ≥ v3.8.1</span>
        </div>
        <div class="form-row"><label>静默期(分)</label><input type="number" min="1" max="1440" v-model="editing.settleMinutes" placeholder="留空 = 立即同步"></div>
        <div class="form-row"><label>包含正则</label><input type="text" v-model="editing.includePattern" placeholder="匹配相对路径(/分隔)，留空不启用"></div>
        <div class="form-row"><label>排除正则</label><input type="text" v-model="editing.excludePattern" placeholder="匹配相对路径(/分隔)，留空不启用"></div>
        <div class="form-row"><label>删除同步</label>
          <div class="switch-label"><div :class="['switch', editing.deleteSync ? 'on' : '']" @click="editing.deleteSync = !editing.deleteSync"></div>开启</div>
          <span class="hint">本地删除→思源旧文档删除（可从思源文件历史恢复）；首轮清理历史残留；排除正则的文件本地删除后同样会被清理</span>
        </div>
        <div class="form-row"><label></label>
          <div class="switch-label"><div :class="['switch', editing.enabled ? 'on' : '']" @click="editing.enabled = !editing.enabled"></div>启用</div>
        </div>
        <div class="form-actions">
          <button class="btn" @click="close">取消</button>
          <button class="btn btn-primary" @click="save">保存</button>
        </div>
      </div>
    </div>
  </div>`,
};

/* ======================= 日志页 ======================= */
const LogsPage = {
  data: () => ({
    projects: [], project: '', from: '', to: '',
    rows: [], hasMore: false, offset: 0, loading: false,
    expanded: {}, detailsCache: {},
  }),
  mounted() {
    this.loadProjects();
    this.query();
  },
  methods: {
    async loadProjects() {
      try {
        const list = await api.get('/api/projects');
        this.projects = Array.isArray(list) ? list : [];
      } catch { /* 下拉选项加载失败不阻塞日志查询 */ }
    },
    async query() {
      this.offset = 0; this.rows = []; this.expanded = {};
      await this.fetchPage();
    },
    more() { return this.fetchPage(); },
    async fetchPage() {
      this.loading = true;
      try {
        const q = new URLSearchParams();
        if (this.project) q.set('project', this.project);
        if (this.from) q.set('from', this.from);
        if (this.to) q.set('to', this.to);
        q.set('limit', LOG_PAGE);
        q.set('offset', this.offset);
        const data = await api.get('/api/sync/history?' + q);
        const runs = (data && data.runs) || [];
        for (const run of runs) {
          for (const p of run.projects || []) {
            this.rows.push({
              runId: run.runId, project: p.project, startedAt: p.startedAt,
              success: p.success, skipped: p.skipped, failed: p.failed, deleted: p.deleted, status: p.status, error: p.error,
            });
          }
        }
        this.offset += LOG_PAGE;
        this.hasMore = !!(data && data.hasMore);
      } catch (e) { alert('加载失败：' + e.message); }
      this.loading = false;
    },
    async toggle(row) {
      const key = row.runId + '|' + row.project;
      if (this.expanded[key]) { this.expanded[key] = false; return; }
      try {
        if (!this.detailsCache[row.runId]) {
          const d = await api.get('/api/sync/history/' + encodeURIComponent(row.runId) + '/details');
          this.detailsCache[row.runId] = (d && d.details) || [];
        }
        this.expanded[key] = true;
      } catch (e) { alert('加载明细失败：' + e.message); }
    },
    detailsOf(row) { return (this.detailsCache[row.runId] || []).filter((d) => d.project === row.project); },
  },
  template: `
  <div class="card">
    <div class="sec-h">同步日志</div>
    <div class="filters">
      <label>项目
        <select v-model="project">
          <option value="">全部</option>
          <option v-for="p in projects" :key="p.name" :value="p.name">{{ p.name }}</option>
        </select>
      </label>
      <label>开始日期 <input type="date" v-model="from"></label>
      <label>至 <input type="date" v-model="to"></label>
      <button class="btn btn-primary" @click="query">查询</button>
    </div>
    <table v-if="rows.length">
      <thead><tr><th>项目</th><th>开始</th><th>成功</th><th>跳过</th><th>失败</th><th>已删</th><th>状态</th><th>错误</th><th>明细</th></tr></thead>
      <tbody>
        <template v-for="(r, i) in rows" :key="i">
          <tr>
            <td>{{ r.project }}</td>
            <td class="mono">{{ fmtDate(r.startedAt) }}</td>
            <td>{{ r.success }}</td>
            <td>{{ r.skipped }}</td>
            <td>{{ r.failed }}</td>
            <td>{{ r.deleted ?? 0 }}</td>
            <td><span :class="statusBadge(r.status).cls">{{ statusBadge(r.status).text }}</span></td>
            <td>{{ r.error || '' }}</td>
            <td><button class="btn-link" @click="toggle(r)">{{ expanded[r.runId + '|' + r.project] ? '收起' : '展开' }}</button></td>
          </tr>
          <tr class="expanded-row" v-if="expanded[r.runId + '|' + r.project]">
            <td colspan="9">
              <table class="sub-table" v-if="detailsOf(r).length">
                <thead><tr><th>相对路径</th><th>结果</th><th>错误</th></tr></thead>
                <tbody>
                  <tr v-for="f in detailsOf(r)" :key="f.relPath">
                    <td><code>{{ f.relPath }}</code></td>
                    <td><span :class="'outcome-' + f.outcome">{{ outcomeLabel(f.outcome) }}</span></td>
                    <td>{{ f.error || '' }}</td>
                  </tr>
                </tbody>
              </table>
              <p v-else class="result-line">无文件级明细（项目整体失败）</p>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
    <p class="result-line" v-else-if="!loading">无匹配的同步记录</p>
    <div class="pager" v-if="hasMore">
      <button class="btn" :disabled="loading" @click="more">加载更多</button>
      <span>已显示 {{ rows.length }} 条</span>
    </div>
    <p class="result-line">记录每轮同步结果；点击[明细]查看该轮文件级结果（新建 / 更新 / 跳过 / 失败 / 删除）。历史数据中未区分新建与更新的显示为"成功"。</p>
  </div>`,
};

/* ======================= 设置页 ======================= */
const SettingsPage = {
  data: () => ({
    form: { serverUrl: '', token: '', defaultNotebook: '', exePath: '', intervalMinutes: null, runOnStart: false, autoStartOnSync: false },
    hasToken: false, webPort: null, mcpEnabled: false,
    autostart: { supported: true, enabled: false },
    siyuanResult: '', syncResult: '', mcpResult: '', autoResult: '',
    testing: false, saving: false,
    maskConst: MASK,
  }),
  computed: {
    mcpUrl() { return 'http://127.0.0.1:' + (this.webPort || 61122) + '/mcp'; },
  },
  mounted() { this.load(); },
  methods: {
    async load() {
      try {
        const [cfg, auto] = await Promise.all([api.get('/api/config'), api.get('/api/system/autostart')]);
        this.form.serverUrl = (cfg.siyuan && cfg.siyuan.serverUrl) ?? '';
        // token 脱敏：服务端返回 "********"（已设置）或 ""（未设置）；表单显示空，避免误导用户编辑
        this.form.token = '';
        this.hasToken = (cfg.siyuan && cfg.siyuan.token) === MASK;
        this.form.defaultNotebook = (cfg.siyuan && cfg.siyuan.defaultNotebook) ?? '';
        this.form.exePath = (cfg.siyuan && cfg.siyuan.exePath) ?? '';
        this.form.autoStartOnSync = !!(cfg.siyuan && cfg.siyuan.autoStartOnSync);
        this.form.intervalMinutes = (cfg.sync && cfg.sync.intervalMinutes) ?? null;
        this.form.runOnStart = !!(cfg.sync && cfg.sync.runOnStart);
        this.mcpEnabled = !!(cfg.mcp && cfg.mcp.enabled);
        this.webPort = (cfg.web && cfg.web.port) ?? null;
        this.autostart = auto;
      } catch (e) { alert('加载失败：' + e.message); }
    },
    configBody() {
      return {
        serverUrl: this.form.serverUrl.trim() || null,
        defaultNotebook: this.form.defaultNotebook.trim() || null,
        siyuanExePath: this.form.exePath.trim(), // 空串=恢复自动搜索，须原样发送不能用 || null
        autoStartOnSync: this.form.autoStartOnSync,
        intervalMinutes: Number(this.form.intervalMinutes) || null,
        runOnStart: this.form.runOnStart,
        // 用户没改 token 字段：留空，服务端保留原值
        token: this.form.token ? this.form.token : null,
      };
    },
    saveSiyuan() { return this.saveConfig('siyuanResult', '配置已保存'); },
    saveSync() { return this.saveConfig('syncResult', '同步设置已保存'); },
    async saveConfig(key, okMsg) {
      this.saving = true;
      try {
        await api.put('/api/config', this.configBody());
        this[key] = okMsg;
        await this.load();
      } catch (e) { this[key] = '保存失败：' + e.message; }
      this.saving = false;
    },
    async testSiyuan() {
      this.testing = true;
      this.siyuanResult = '测试连接中…';
      try {
        const r = await api.post('/api/siyuan/test');
        if (r && r.ok) {
          const list = (r.notebooks || []).join('，') || '（无笔记本）';
          this.siyuanResult = '连接成功，笔记本：' + list;
        } else {
          this.siyuanResult = '连接失败：' + ((r && (r.message || r.details)) || '未知错误');
        }
      } catch (e) { this.siyuanResult = '连接失败：' + e.message; }
      this.testing = false;
    },
    async saveMcp() {
      try {
        await api.put('/api/config', { mcpEnabled: this.mcpEnabled });
        this.mcpResult = 'MCP 设置已保存（启用状态变更需重启程序生效）';
        await this.load();
      } catch (e) { this.mcpResult = '保存失败：' + e.message; }
    },
    async saveAutostart() {
      try {
        const r = await api.put('/api/system/autostart', { enabled: this.autostart.enabled });
        if (r && r.ok) this.autoResult = r.enabled ? '已开启开机自启。' : '已关闭开机自启。';
        else this.autoResult = '保存失败：' + ((r && r.error) || '未知错误');
      } catch (e) { this.autoResult = '保存失败：' + e.message; }
    },
  },
  template: `
  <div>
    <div class="card">
      <div class="sec-h">思源连接</div>
      <div class="form-row"><label>Server URL</label><input type="text" v-model="form.serverUrl" placeholder="http://127.0.0.1:6806"></div>
      <div class="form-row"><label>Token</label><input type="password" v-model="form.token" :placeholder="hasToken ? maskConst : '未设置（留空）'" autocomplete="off"></div>
      <div class="form-row"><label>默认笔记本</label><input type="text" v-model="form.defaultNotebook"></div>
      <div class="form-row"><label>思源 exe 路径</label><input type="text" v-model="form.exePath" placeholder="留空自动搜索（NSIS / siyuan:// 协议 / Microsoft Store）"></div>
      <div class="toolbar" style="margin-top:14px">
        <button class="btn btn-primary" :disabled="saving" @click="saveSiyuan">保存配置</button>
        <button class="btn" :disabled="testing" @click="testSiyuan">测试连接</button>
        <span class="result" v-if="siyuanResult">{{ siyuanResult }}</span>
      </div>
    </div>

    <div class="card">
      <div class="sec-h">同步节奏</div>
      <div class="form-row"><label>间隔（分钟）</label><input type="number" min="1" step="1" v-model.number="form.intervalMinutes"></div>
      <div class="form-row"><label></label>
        <div class="switch-label"><div :class="['switch', form.runOnStart ? 'on' : '']" @click="form.runOnStart = !form.runOnStart"></div>启动时立即跑一轮</div>
      </div>
      <div class="form-row"><label></label>
        <div class="switch-label"><div :class="['switch', form.autoStartOnSync ? 'on' : '']" @click="form.autoStartOnSync = !form.autoStartOnSync"></div>思源未运行时自动启动（隐藏窗口，就绪后同步）</div>
      </div>
      <div class="toolbar" style="margin-top:14px">
        <button class="btn btn-primary" :disabled="saving" @click="saveSync">保存</button>
        <span class="result" v-if="syncResult">{{ syncResult }}</span>
      </div>
    </div>

    <div class="card">
      <div class="sec-h">MCP 服务（Model Context Protocol）</div>
      <div class="form-row"><label></label>
        <div class="switch-label"><div :class="['switch', mcpEnabled ? 'on' : '']" @click="mcpEnabled = !mcpEnabled"></div>启用 MCP 服务</div>
      </div>
      <div class="form-row"><label>MCP 服务地址</label><input type="text" readonly :value="mcpUrl"></div>
      <div class="toolbar" style="margin-top:14px">
        <button class="btn btn-primary" @click="saveMcp">保存 MCP 设置</button>
        <span class="result" v-if="mcpResult">{{ mcpResult }}</span>
      </div>
      <div class="note">供 AI 客户端（Claude Desktop / Cursor 等）调用，仅暴露"新增项目"工具，与 Web 管理页共用同一端口，仅本机可调用。Streamable HTTP：POST /mcp。启用状态变更需重启程序。</div>
    </div>

    <div class="card">
      <div class="sec-h">开机自启</div>
      <div class="form-row"><label></label>
        <div class="switch-label">
          <div :class="['switch', autostart.enabled ? 'on' : '']" @click="autostart.supported && (autostart.enabled = !autostart.enabled)"></div>开机自动启动
        </div>
      </div>
      <div class="toolbar" style="margin-top:14px">
        <button class="btn btn-primary" :disabled="!autostart.supported" @click="saveAutostart">保存</button>
        <span class="result" v-if="autoResult">{{ autoResult }}</span>
      </div>
      <div class="note" v-if="!autostart.supported">当前系统不支持开机自启（仅 Windows）。</div>
      <div class="note" v-else>登录 Windows 时自动启动本程序（写入注册表 HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run）。仅 Windows 支持。</div>
    </div>
  </div>`,
};

/* ======================= 关于页 ======================= */
const AboutPage = {
  data: () => ({ info: {}, checking: false, updateInfo: null, result: '' }),
  mounted() { this.load(); },
  methods: {
    async load() {
      try { this.info = (await api.get('/api/system/info')) || {}; } catch { /* 版本号缺省 … */ }
    },
    async check() {
      this.checking = true;
      this.result = '检查中…';
      try {
        const r = await api.post('/api/system/update/check');
        if (r && r.ok === false) {
          this.result = '检查失败：' + (r.error || '未知错误');
          this.updateInfo = null;
        } else if (!r.hasUpdate) {
          this.result = '已是最新版本（' + r.currentVersion + '）。';
          this.updateInfo = null;
        } else {
          this.updateInfo = r;
          const sizeMb = (r.sizeBytes / 1024 / 1024).toFixed(1);
          this.result = '发现新版本 ' + r.latestVersion + '（约 ' + sizeMb + ' MB）。点击"应用更新"下载并重启升级。';
        }
      } catch (e) { this.result = '检查失败：' + e.message; }
      this.checking = false;
    },
    async apply() {
      if (!this.updateInfo) return;
      if (!confirm('确认升级到 ' + this.updateInfo.latestVersion + '？\n程序将下载升级包、退出并由升级程序重启。')) return;
      this.result = '下载中，程序即将退出并重启…';
      try {
        const r = await api.post('/api/system/update/apply');
        if (r && r.ok === false) this.result = '升级失败：' + (r.error || '未知错误');
        else this.result = '升级已启动，程序即将重启，本页面会短暂不可用…';
      } catch (e) { this.result = '升级失败：' + e.message; }
    },
    openRepo() { if (this.info.repoUrl) window.open(this.info.repoUrl, '_blank'); },
  },
  template: `
  <div class="card">
    <div class="sec-h">关于</div>
    <div class="form-row"><label>当前版本</label><input type="text" readonly :value="info.version || ''"></div>
    <div class="form-row"><label>仓库地址</label><input type="text" readonly :value="info.repoUrl || ''"></div>
    <div class="toolbar" style="margin-top:14px">
      <button class="btn btn-primary" :disabled="checking" @click="check">检查更新</button>
      <button class="btn" :disabled="!updateInfo" @click="apply">应用更新</button>
      <button class="btn" @click="openRepo">打开仓库</button>
    </div>
    <p class="result-line" v-if="result">{{ result }}</p>
  </div>`,
};

/* ======================= 根实例 ======================= */
const App = {
  data: () => ({
    tab: 'overview',
    info: {},
    running: false,
    tabs: [
      { id: 'overview', name: '概览' },
      { id: 'projects', name: '项目' },
      { id: 'logs', name: '日志' },
      { id: 'settings', name: '设置' },
      { id: 'about', name: '关于' },
    ],
  }),
  computed: {
    uptimeText() { return fmtUptime(this.info.uptimeSeconds); },
  },
  mounted() { this.loadInfo(); },
  methods: {
    async loadInfo() {
      try { this.info = (await api.get('/api/system/info')) || {}; } catch { /* banner 显示 … 占位 */ }
    },
    onRunning() { this.running = true; },
  },
};

const app = Vue.createApp(App);
// 模板内共享的格式化/映射函数
app.config.globalProperties.fmtDate = fmtDate;
app.config.globalProperties.fmtBytes = fmtBytes;
app.config.globalProperties.outcomeLabel = outcomeLabel;
app.config.globalProperties.sortModeLabel = sortModeLabel;
app.config.globalProperties.statusBadge = statusBadge;
app.component('overview-page', OverviewPage);
app.component('projects-page', ProjectsPage);
app.component('logs-page', LogsPage);
app.component('settings-page', SettingsPage);
app.component('about-page', AboutPage);
app.mount('#app');
