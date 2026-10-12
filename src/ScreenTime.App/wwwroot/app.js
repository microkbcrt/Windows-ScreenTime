"use strict";

/* ---------- RPC ---------- */
let _id = 0;
const _pend = new Map();
function rpc(name, args = {}) {
  return new Promise((resolve) => {
    const id = ++_id;
    _pend.set(id, resolve);
    window.chrome.webview.postMessage({ id, name, args });
  });
}
window.chrome.webview.addEventListener("message", (e) => {
  const m = e.data;
  if (!m || typeof m !== "object") return;
  if (m.type === "refresh") { loadAll(); return; }
  if (m.type === "update-available") { showUpdateModal(m.version, m.notes); return; }
  if (m.type === "update-check-result") {
    if (m.hasUpdate) showUpdateModal(m.version, null);
    else if (m.error) alert("检查更新失败，请检查网络后重试");
    else alert("当前已是最新版本 v" + m.version);
    return;
  }
  if (m.type === "update-progress") {
    showUpdateProgress();
    document.getElementById("upd-pct").textContent = m.percent + "%";
    document.getElementById("upd-bar").style.width = m.percent + "%";
    return;
  }
  if (m.type === "update-stage") {
    const el = document.getElementById("upd-status");
    if (el) el.textContent = m.stage;
    return;
  }
  if (m.type === "update-finished") {
    if (m.ok) {
      showUpdateProgress();
      document.getElementById("upd-status").textContent = "安装已启动，应用即将自动重启…";
    } else {
      document.getElementById("update-progress").hidden = true;
      alert("更新失败，请稍后重试");
    }
    return;
  }
  const r = _pend.get(m.id);
  if (r) { _pend.delete(m.id); r(m.data); }
});
window.addEventListener("error", (e) => {
  try { window.chrome.webview.postMessage({ id: 0, name: "jserror", args: { msg: String(e.message), at: String(e.filename) + ":" + e.lineno } }); } catch (_) {}
});

/* ---------- 自更新 ---------- */
function showUpdateModal(version, notes) {
  document.getElementById("upd-ver").textContent = "v" + version;
  document.getElementById("upd-notes").textContent = notes || "";
  document.getElementById("update-modal").hidden = false;
}
function showUpdateProgress() {
  document.getElementById("update-modal").hidden = true;
  document.getElementById("update-progress").hidden = false;
}
document.getElementById("upd-later").addEventListener("click", () => {
  document.getElementById("update-modal").hidden = true;
});
document.getElementById("upd-go").addEventListener("click", async () => {
  await rpc("doupdate");
});
document.getElementById("check-update-btn").addEventListener("click", () => {
  rpc("checkupdate");
});
document.getElementById("opendata-btn").addEventListener("click", () => {
  rpc("opendata");
});

/* ---------- 工具 ---------- */
function fmtDur(sec) {
  sec = Math.round(sec);
  if (sec < 60) return sec + " 秒";
  const h = Math.floor(sec / 3600), m = Math.round((sec % 3600) / 60);
  if (h === 0) return m + " 分钟";
  return m > 0 ? `${h} 小时 ${m} 分钟` : `${h} 小时`;
}
const CAT_COLORS = {
  "社交": "#0A84FF", "娱乐": "#FF9F0A", "游戏": "#FF375F",
  "办公": "#30D158", "开发": "#5E5CE6", "浏览器": "#64D2FF",
  "系统": "#8E8E93", "其他": "#C7C7CC",
};
let customColorMap = {};
let builtinCats = [];
function catColor(c) {
  return CAT_COLORS[c] || customColorMap[c] ||
    PALETTE[(c.length * 7 + (c.charCodeAt(0) || 0)) % PALETTE.length];
}
function iconColor(name) {
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return PALETTE[h % PALETTE.length];
}
function esc(s) { return (s ?? "").toString().replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }
function shortName(name) {
  for (const ch of name) if (/[\u4e00-\u9fa5A-Za-z0-9]/.test(ch)) return ch.toUpperCase();
  return "?";
}

/* ---------- 图表 ---------- */
const charts = {};
function chart(id) {
  if (!charts[id]) charts[id] = echarts.init(document.getElementById(id), null, { renderer: "canvas" });
  return charts[id];
}
window.addEventListener("resize", () => Object.values(charts).forEach(c => c.resize()));

const GRID = { left: 34, right: 14, top: 26, bottom: 26 };

/* ---------- 数据加载 ---------- */
let currentView = "dashboard";
let currentRange = "day";
let detailApp = null;

async function loadAll() {
  try {
    if (currentView === "dashboard") await loadDashboard();
    if (currentView === "apps") await loadApps();
    if (currentView === "settings") await loadSettings();
    if (detailApp) await loadDetail(detailApp);
  } catch (err) { console.error(err); }
}

async function loadLive() {
  const d = await rpc("live");
  const app = d.active ? (d.app || "未知应用") : "已暂停计时";
  document.getElementById("live-app").textContent = app;
  const t = d.active ? (d.title || "") : (d.app ? `空闲中 — ${d.app}` : "等待活动");
  document.getElementById("live-title").textContent = t;
  document.getElementById("live-dot").className = d.active ? "dot on" : "dot off";
}

async function loadDashboard() {
  const d = await rpc("overview");

  document.getElementById("total").textContent = fmtDur(d.todaySeconds);
  const diff = d.todaySeconds - d.yesterdaySeconds;
  document.getElementById("delta").textContent =
    d.yesterdaySeconds > 0
      ? (diff >= 0 ? `较昨日 +${fmtDur(diff)}` : `较昨日 -${fmtDur(-diff)}`)
      : "暂无昨日数据";
  document.getElementById("pickups").textContent = d.pickups;
  document.getElementById("week").textContent = fmtDur(d.weekTotal);
  document.getElementById("topapp").textContent = d.topApp || "—";
  document.getElementById("topapp").title = d.topApp || "";

  chart("hourly").setOption({
    grid: GRID,
    tooltip: { trigger: "axis", valueFormatter: v => fmtDur(v) },
    xAxis: { type: "category", data: [...Array(24).keys()].map(h => h + "时"), axisLabel: { fontSize: 10, interval: 2 } },
    yAxis: { type: "value", axisLabel: { fontSize: 10, formatter: v => Math.round(v / 60) + "分" } },
    series: [{ type: "bar", data: d.hourly, itemStyle: { color: "#0A84FF", borderRadius: [4, 4, 0, 0] } }],
  });

  chart("trend").setOption({
    grid: GRID,
    tooltip: { trigger: "axis", valueFormatter: v => fmtDur(v) },
    xAxis: { type: "category", data: d.trend.map(t => t.label), axisLabel: { fontSize: 10 } },
    yAxis: { type: "value", axisLabel: { fontSize: 10, formatter: v => Math.round(v / 3600) + "时" } },
    series: [{ type: "bar", data: d.trend.map(t => t.seconds), itemStyle: { color: "#5E5CE6", borderRadius: [4, 4, 0, 0] } }],
  });

  const totals = await rpc("apps", { range: "day" });
  renderRank(document.getElementById("rank"), totals.rows.slice(0, 8), totals.totalSeconds);

  const catMap = {};
  for (const r of totals.rows) catMap[r.category] = (catMap[r.category] || 0) + r.seconds;
  const catData = Object.entries(catMap).map(([k, v]) => ({ name: k, value: Math.round(v) }))
    .sort((a, b) => b.value - a.value);
  chart("cat").setOption({
    tooltip: { trigger: "item", valueFormatter: v => fmtDur(v) },
    legend: { bottom: 0, icon: "circle", textStyle: { fontSize: 11 } },
    series: [{
      type: "pie", radius: ["48%", "72%"], center: ["50%", "44%"],
      label: { show: false },
      data: catData.map(x => ({ ...x, itemStyle: { color: catColor(x.name) } })),
    }],
  });
}

function renderRank(container, rows, total) {
  if (!rows.length) { container.innerHTML = '<div class="empty">暂无数据，去使用一会儿电脑吧</div>'; return; }
  const max = Math.max(...rows.map(r => r.seconds), 1);
  container.innerHTML = rows.map((r, i) => `
    <div class="rank-item" data-app="${esc(r.app)}">
      <div class="rank-icon" style="background:${iconColor(r.name)}">${esc(shortName(r.name))}</div>
      <div class="rank-main">
        <div class="rank-top">
          <span class="rank-name">${i + 1}. ${esc(r.name)}</span>
          <span class="rank-time">${fmtDur(r.seconds)}</span>
        </div>
        <div class="rank-track"><div class="rank-fill" style="width:${Math.max(2, r.seconds / max * 100)}%;background:${iconColor(r.name)}"></div></div>
      </div>
    </div>`).join("");
  container.querySelectorAll(".rank-item").forEach(el =>
    el.addEventListener("click", () => openDetail(el.dataset.app)));
}

async function loadApps() {
  const d = await rpc("apps", { range: currentRange });
  const tbody = document.querySelector("#apps-table tbody");
  if (!d.rows.length) {
    tbody.innerHTML = '<tr><td colspan="5"><div class="empty">暂无数据</div></td></tr>';
    return;
  }
  const max = Math.max(...d.rows.map(r => r.seconds), 1);
  tbody.innerHTML = d.rows.map(r => `
    <tr>
      <td><div class="app-cell">
        <div class="rank-icon" style="background:${iconColor(r.name)}">${esc(shortName(r.name))}</div>${esc(r.name)}</div></td>
      <td><span class="cat-tag" style="color:${catColor(r.category)}">${esc(r.category)}</span></td>
      <td><b>${fmtDur(r.seconds)}</b></td>
      <td><div class="pct-track"><div class="pct-fill" style="width:${Math.max(2, r.seconds / max * 100)}%"></div></div>
          <span class="sub" style="margin-top:3px;display:inline-block">${r.percent}%</span></td>
      <td>${r.limitMinutes ? `<span class="cat-tag" style="color:#FF9F0A">${r.limitMinutes} 分钟/天</span> ` : ""}<a class="link" data-app="${esc(r.app)}">详情 / 限额</a></td>
    </tr>`).join("");
  tbody.querySelectorAll("a.link").forEach(el =>
    el.addEventListener("click", () => openDetail(el.dataset.app)));
}

async function loadSettings() {
  const d = await rpc("settings");
  document.getElementById("idle-select").value = String(d.idleSeconds);
  document.getElementById("autostart").checked = d.autostart;
  document.getElementById("datadir").textContent = d.dataDir;
  document.getElementById("about").textContent = `Windows屏幕使用时间 v${d.version} · 作者 ${d.author}`;

  const apps = await rpc("apps", { range: "month" });
  const sel = document.getElementById("limit-app");
  const cur = sel.value;
  sel.innerHTML = '<option value="">选择应用…</option>' +
    apps.rows.map(r => `<option value="${esc(r.app)}">${esc(r.name)}</option>`).join("");
  if (cur) sel.value = cur;

  const tbody = document.querySelector("#limits-table tbody");
  tbody.innerHTML = d.limits.length
    ? d.limits.map(l => `
      <tr>
        <td><b>${esc(l.name)}</b></td>
        <td>${l.minutes} 分钟/天</td>
        <td><a class="link" data-app="${esc(l.app)}">删除</a></td>
      </tr>`).join("")
    : '<tr><td colspan="3"><div class="empty">尚未设置任何限额</div></td></tr>';
  tbody.querySelectorAll("a.link").forEach(el =>
    el.addEventListener("click", async () => {
      await rpc("setlimit", { app: el.dataset.app, minutes: null });
      loadSettings();
    }));
}

async function loadDetail(app) {
  const d = await rpc("appdetail", { app });
  document.getElementById("md-name").textContent = d.name;
  const catSel = document.getElementById("md-cat-select");
  fillCatSelect(d.customCategory ? d.category : "自动");
  catSel.onchange = async () => {
    await rpc("setcategory", { app: d.app, category: catSel.value === "自动" ? null : catSel.value });
    loadAll();
  };
  document.getElementById("md-limit").value = d.limitMinutes ?? 0;
  chart("md-trend").setOption({
    grid: { ...GRID, left: 44 },
    tooltip: { trigger: "axis", valueFormatter: v => fmtDur(v) },
    xAxis: { type: "category", data: d.trend.map(t => t.label), axisLabel: { fontSize: 10 } },
    yAxis: { type: "value", axisLabel: { fontSize: 10, formatter: v => Math.round(v / 60) + "分" } },
    series: [{ type: "bar", data: d.trend.map(t => t.seconds), itemStyle: { color: "#0A84FF", borderRadius: [4, 4, 0, 0] } }],
  });
  const sites = document.getElementById("md-sites");
  sites.innerHTML = d.sites.length
    ? d.sites.map((s, i) => `
      <div class="rank-item" style="cursor:default">
        <div class="rank-icon" style="background:${catColor("浏览器")}">${i + 1}</div>
        <div class="rank-main">
          <div class="rank-top"><span class="rank-name">${esc(s.site)}</span>
          <span class="rank-time">${fmtDur(s.seconds)}</span></div>
        </div>
      </div>`).join("")
    : '<div class="empty">今天没有网站级记录（仅浏览器有网站统计）</div>';
}

/* ---------- 弹窗 ---------- */
async function openDetail(app) {
  detailApp = app;
  document.getElementById("modal").hidden = false;
  chart("md-trend").resize();
  await loadDetail(app);
}
function closeDetail() {
  detailApp = null;
  document.getElementById("modal").hidden = true;
}
document.getElementById("md-close").addEventListener("click", closeDetail);
document.getElementById("modal").addEventListener("click", e => { if (e.target.id === "modal") closeDetail(); });
document.getElementById("md-limit-save").addEventListener("click", async () => {
  if (!detailApp) return;
  const v = parseInt(document.getElementById("md-limit").value, 10);
  await rpc("setlimit", { app: detailApp, minutes: isNaN(v) || v <= 0 ? null : v });
  closeDetail();
  loadAll();
});

/* ---------- 导航 ---------- */
document.querySelectorAll("#nav nav a").forEach(a =>
  a.addEventListener("click", () => {
    document.querySelectorAll("#nav nav a").forEach(x => x.classList.remove("active"));
    a.classList.add("active");
    currentView = a.dataset.view;
    for (const id of ["dashboard", "apps", "settings"])
      document.getElementById("view-" + id).hidden = id !== currentView;
    loadAll();
  }));

document.querySelectorAll(".range-tabs button").forEach(b =>
  b.addEventListener("click", () => {
    document.querySelectorAll(".range-tabs button").forEach(x => x.classList.remove("active"));
    b.classList.add("active");
    currentRange = b.dataset.range;
    loadApps();
  }));

/* ---------- 设置操作 ---------- */
document.getElementById("idle-select").addEventListener("change", async e => {
  await rpc("setidle", { seconds: parseInt(e.target.value, 10) });
});
document.getElementById("autostart").addEventListener("change", async e => {
  await rpc("setautostart", { enabled: e.target.checked });
});
document.getElementById("export-btn").addEventListener("click", async () => {
  const d = await rpc("export");
  if (d && d.path) alert("已导出到桌面：\n" + d.path);
});
document.getElementById("exportdata-btn").addEventListener("click", async () => {
  const d = await rpc("exportdata");
  if (d && d.ok) alert("备份已导出到：\n" + d.path);
  else if (d && d.error) alert("导出失败：" + d.error);
});
document.getElementById("importdata-btn").addEventListener("click", async () => {
  if (!confirm("导入备份将覆盖当前全部数据，应用会自动重启。\n（当前数据会自动留一份「导入前」备份）\n\n确定继续吗？")) return;
  const pick = await rpc("pickimport");
  if (!pick || !pick.ok) return;
  const r = await rpc("importdata", { path: pick.path });
  if (r && r.ok) {
    alert("导入成功，应用即将重启。");
    await rpc("restart");
  } else if (r && r.error) {
    alert("导入失败：" + r.error);
  }
});
document.getElementById("limit-add").addEventListener("click", async () => {
  const app = document.getElementById("limit-app").value;
  const min = parseInt(document.getElementById("limit-minutes").value, 10);
  if (!app || isNaN(min) || min <= 0) { alert("请选择应用并填写分钟数"); return; }
  await rpc("setlimit", { app, minutes: min });
  document.getElementById("limit-minutes").value = "";
  loadSettings();
});

/* ---------- 自定义分类 ---------- */
function fillCatSelect(value) {
  const sel = document.getElementById("md-cat-select");
  sel.innerHTML = '<option value="自动">分类：自动识别（内置规则）</option>'
    + builtinCats.map(c => `<option value="${esc(c)}">${esc(c)}</option>`).join("")
    + Object.keys(customColorMap).map(c => `<option value="${esc(c)}">${esc(c)}</option>`).join("");
  sel.value = value;
}
async function loadCategories() {
  const d = await rpc("categories");
  builtinCats = d.builtin || [];
  customColorMap = {};
  (d.custom || []).forEach(c => customColorMap[c.name] = c.color);
  const cur = document.getElementById("md-cat-select").value;
  fillCatSelect(cur || "自动");
  const list = document.getElementById("cats-list");
  const custom = d.custom || [];
  list.innerHTML = custom.length
    ? custom.map(c => `
      <div class="rank-item" style="cursor:default">
        <span class="cat-dot" style="background:${esc(c.color)}"></span>
        <div class="rank-main"><span class="rank-name">${esc(c.name)}</span></div>
        <a class="link" data-cat="${esc(c.name)}">删除</a>
      </div>`).join("")
    : '<div class="empty">暂无自定义分类</div>';
  list.querySelectorAll("a.link").forEach(el =>
    el.addEventListener("click", async () => {
      if (!confirm(`删除分类「${el.dataset.cat}」？使用它的应用将回退为自动分类。`)) return;
      await rpc("delcategory", { name: el.dataset.cat });
      await loadCategories();
      loadAll();
    }));
}
async function addCategory(name, applyApp) {
  if (!name) { alert("请输入分类名称"); return false; }
  const r = await rpc("addcategory", { name });
  if (!r || !r.ok) { alert(r && r.error || "创建失败"); return false; }
  if (applyApp) await rpc("setcategory", { app: applyApp, category: name });
  await loadCategories();
  loadAll();
  return true;
}
document.getElementById("md-addcat").addEventListener("click", async () => {
  if (!detailApp) return;
  const input = document.getElementById("md-newcat");
  if (await addCategory(input.value.trim(), detailApp)) input.value = "";
});
document.getElementById("add-cat-btn").addEventListener("click", async () => {
  const input = document.getElementById("new-cat-name");
  if (await addCategory(input.value.trim(), null)) input.value = "";
});

/* ---------- 启动 ---------- */
(async () => {
  await loadCategories();
  loadAll();
  loadLive();
  setInterval(loadLive, 3000);
})();
