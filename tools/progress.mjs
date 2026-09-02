#!/usr/bin/env node
// Regenerates progress.html from gauntlet/**/round-*.json and gauntlet/status.json.
// Static output, no deps. Usage: node tools/progress.mjs
import { readFileSync, readdirSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const gdir = join(root, 'gauntlet');
const PIECES = ['harness','town','odm','camera','mikasa','titan','boss','combat','ai','destruction','look','hud','audio','encounter','performance'];

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (/^round-\d+\.json$/.test(e.name)) out.push(p);
  }
  return out;
}
const rounds = walk(gdir).map(p => { try { return { ...JSON.parse(readFileSync(p, 'utf8')), _file: relative(root, p) }; } catch { return null; } }).filter(Boolean);
let status = { wave: 0, pieces: {} };
try { status = JSON.parse(readFileSync(join(gdir, 'status.json'), 'utf8')); } catch {}

const byPiece = {};
for (const r of rounds) (byPiece[r.piece] ??= []).push(r);
for (const k in byPiece) byPiece[k].sort((a, b) => a.round - b.round);
const names = [...new Set([...PIECES, ...Object.keys(status.pieces || {}), ...Object.keys(byPiece)])];

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function sparkline(rs) {
  const w = 160, h = 36, n = rs.length;
  if (!n) return `<svg class="spark" viewBox="0 0 ${w} ${h}"><text x="0" y="24" class="muted">no rounds yet</text></svg>`;
  const pts = rs.map((r, i) => [n === 1 ? w / 2 : (i / (n - 1)) * (w - 8) + 4, h - 4 - (Math.max(0, Math.min(10, +r.score || 0)) / 10) * (h - 8)]);
  const line = pts.map(p => p.map(v => v.toFixed(1)).join(',')).join(' ');
  const dots = pts.map((p, i) => `<circle cx="${p[0].toFixed(1)}" cy="${p[1].toFixed(1)}" r="3" class="${rs[i].verdict === 'win' ? 'win' : 'lose'}"><title>r${rs[i].round}: ${rs[i].score}/10 ${rs[i].verdict}</title></circle>`).join('');
  return `<svg class="spark" viewBox="0 0 ${w} ${h}"><polyline points="${line}"/>${dots}</svg>`;
}
function latestShot(piece, last) {
  const committed = `shots/${piece}/latest.png`;
  if (existsSync(join(root, committed))) return committed;
  const s = last?.shots?.find(p => existsSync(join(root, p)));
  return s || null;
}
const fmt = ts => { if (!ts) return ''; const d = new Date(typeof ts === 'number' && ts < 1e12 ? ts * 1000 : ts); return isNaN(d) ? String(ts) : d.toISOString().replace('T', ' ').slice(0, 16) + ' UTC'; };
const lastTs = rounds.map(r => new Date(typeof r.ts === 'number' && r.ts < 1e12 ? r.ts * 1000 : r.ts).getTime()).filter(x => !isNaN(x)).sort((a, b) => b - a)[0];

const cards = names.map(piece => {
  const rs = byPiece[piece] || [];
  const last = rs[rs.length - 1];
  const st = status.pieces?.[piece] || {};
  const state = st.state || (last ? (last.verdict === 'win' ? 'won' : 'iterating') : 'queued');
  const score = st.score ?? last?.score;
  const shot = latestShot(piece, last);
  const idx = PIECES.indexOf(piece);
  return `<article class="card state-${esc(state)}">
  <div class="shot">${shot ? `<img src="${esc(shot)}" alt="${esc(piece)} latest shot" loading="lazy">` : `<div class="noshot">no shot</div>`}</div>
  <div class="body">
    <header><span class="num">${idx >= 0 ? idx : '·'}</span><h2>${esc(piece)}</h2><span class="badge ${esc(state)}">${esc(state)}</span></header>
    <div class="row"><span class="score">${score != null ? esc(score) + '<small>/10</small>' : '<small class="muted">–</small>'}</span>${sparkline(rs)}<span class="round muted">${last ? 'r' + last.round : st.round ? 'r' + st.round : ''}</span></div>
    <p class="gap">${last?.gap ? esc(last.gap) : '<span class="muted">no verdict yet</span>'}</p>
    ${last ? `<p class="meta muted">${esc(last._file)} · ${fmt(last.ts)}</p>` : ''}
  </div>
</article>`;
}).join('\n');

const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>AOT FABLE 5.1 progress</title>
<style>
:root{--bg:#0d0f12;--card:#161a20;--line:#242a33;--fg:#e6e8eb;--muted:#7d8590;--win:#3fb950;--lose:#f85149;--accent:#e0a458}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 -apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,Arial,sans-serif}
.top{display:flex;align-items:baseline;gap:18px;padding:18px 24px;border-bottom:1px solid var(--line);position:sticky;top:0;background:var(--bg)}
.top h1{margin:0;font-size:18px;letter-spacing:.04em}.top .wave{color:var(--accent);font-weight:600}.top .upd{margin-left:auto;color:var(--muted)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:14px;padding:18px 24px}
.card{background:var(--card);border:1px solid var(--line);border-radius:8px;overflow:hidden;display:flex;flex-direction:column}
.shot{aspect-ratio:16/9;background:#000;display:flex;align-items:center;justify-content:center}.shot img{width:100%;height:100%;object-fit:cover;display:block}
.noshot{color:var(--muted);font-size:12px;letter-spacing:.1em;text-transform:uppercase}
.body{padding:12px 14px 14px}header{display:flex;align-items:center;gap:10px}header h2{margin:0;font-size:15px;text-transform:capitalize;flex:1}
.num{font-family:ui-monospace,Menlo,monospace;color:var(--muted);font-size:12px;min-width:16px}
.badge{font-size:11px;padding:2px 8px;border-radius:999px;border:1px solid var(--line);color:var(--muted);text-transform:uppercase;letter-spacing:.06em}
.badge.won{border-color:var(--win);color:var(--win)}.badge.iterating,.badge.building{border-color:var(--accent);color:var(--accent)}.badge.blocked{border-color:var(--lose);color:var(--lose)}
.row{display:flex;align-items:center;gap:12px;margin:10px 0 6px}.score{font-size:24px;font-weight:600;min-width:52px}.score small{font-size:12px;color:var(--muted);font-weight:400}
.spark{width:160px;height:36px}.spark polyline{fill:none;stroke:var(--muted);stroke-width:1.5}.spark circle.win{fill:var(--win)}.spark circle.lose{fill:var(--lose)}.spark text{fill:var(--muted);font-size:11px}
.gap{margin:0;font-size:13px}.meta{margin:8px 0 0;font-size:11px}.muted{color:var(--muted)}.round{margin-left:auto;font-family:ui-monospace,Menlo,monospace}
</style></head><body>
<div class="top"><h1>AOT FABLE 5.1</h1><span class="wave">wave ${esc(status.wave ?? 0)}</span><span class="muted">${rounds.length} round${rounds.length === 1 ? '' : 's'} · ${names.length} pieces</span><span class="upd">last verdict ${lastTs ? fmt(lastTs) : '—'} · generated ${fmt(Date.now())}</span></div>
<main class="grid">
${cards}
</main>
</body></html>
`;
writeFileSync(join(root, 'progress.html'), html);
console.log(`progress.html: ${names.length} pieces, ${rounds.length} rounds, wave ${status.wave ?? 0}`);
