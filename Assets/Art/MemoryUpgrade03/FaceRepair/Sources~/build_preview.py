"""Package existing Aseprite PNGs into an offline review player; no image editing."""
from pathlib import Path
import base64
import json

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent / 'Runtime/Animation'
m = json.loads((ART / 'manifest.json').read_text())
paths = {m['base']}
for s in m['states']:
    paths.add(s['patch'])
    paths.update(f['delta'] for f in s['frames'])
for a in m['actions']:
    paths.update(f['delta'] for f in a['frames'])
images = {p: 'data:image/png;base64,' + base64.b64encode((ART / p).read_bytes()).decode() for p in paths}
payload = json.dumps({'manifest': m, 'images': images}, separators=(',', ':'))
html = r'''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>修脸与妆容 · 动画预览</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#141312;color:#d1cbc5;font:15px/1.6 system-ui,"Microsoft YaHei",sans-serif}main{max-width:1230px;margin:auto;padding:24px}h1{font-size:23px;margin:0 0 5px;font-weight:600}p{margin:0;color:#9f9891}.top{margin-bottom:20px}.layout{display:grid;grid-template-columns:minmax(0,864px) 240px;gap:22px}canvas{display:block;width:100%;image-rendering:pixelated;background:#000;border:1px solid #393633}.bar{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin:12px 0}button,select{font:inherit;color:inherit;background:#282522;border:1px solid #514c46;padding:8px 12px;border-radius:4px;cursor:pointer}button:hover,select:hover{border-color:#b1a69a}button.primary{background:#b6aba0;color:#181614;border-color:#b6aba0}button.active{border-color:#ddd0bf;color:#fff}.actions{display:grid;gap:7px}.actions button{text-align:left}.muted{color:#8c857d;font-size:12px}.state-title{margin-bottom:7px;font-size:13px;color:#9e958c}.wide{width:100%}#status{min-height:26px;color:#c9bdb0}#scrub{width:100%;accent-color:#a69a8b}#counter{font-variant-numeric:tabular-nums}.note{margin-top:15px;font-size:13px;color:#aaa097}.divider{height:1px;background:#39332f;margin:16px 0}@media(max-width:840px){.layout{grid-template-columns:1fr}.actions{grid-template-columns:repeat(3,1fr)}main{padding:12px}.actions button{font-size:13px;padding:7px}}
</style><main><div class="top"><h1>修脸与妆容</h1><p>使用你修改的四个手势母版；保留原像素，以平移和接触粒子完成操作。</p></div>
<div class="layout"><section><canvas id="scene" width="216" height="162" style="width:432px" aria-label="角色动画预览"></canvas>
<div class="bar"><button id="all" class="primary">连续播放</button><button id="pause">暂停</button><button id="replay">重播当前动作</button><label>速度 <select id="speed"><option value="0.5">0.5×</option><option value="1" selected>1×</option><option value="2">2×</option><option value="4">4×</option></select></label><label>显示 <select id="zoom"><option value="1">1× 原尺寸</option><option value="2" selected>2×</option><option value="3">3×</option><option value="4">4×</option></select></label></div>
<div id="status" role="status">正在加载…</div><input id="scrub" type="range" min="0" max="1" value="0" disabled aria-label="动作帧"><span id="counter" class="muted"></span>
<p class="note">游戏画布为 216×162 px，图案线宽为 1 px。右眉已拉开与眼睛的间距，嘴巴保留母版。放大显示使用最近邻。</p></section>
<aside><div class="state-title">查看完成状态</div><select id="states" class="wide" aria-label="完成状态"></select><div class="divider"></div><div class="state-title">单独播放动作</div><div id="actions" class="actions"></div><div class="divider"></div><button id="cancel" class="wide">取消动作，恢复原状态</button><p class="muted" style="margin-top:10px">猫咪和爱心是两种可选结果。动作单次播放，完成后的待机循环播放。</p></aside></div></main>
<script>
const DATA=__DATA__;
const M=DATA.manifest, stateMap=Object.fromEntries(M.states.map(s=>[s.id,s]));
const S=['原始状态','擦拭后','修补后','左侧上粉后','两侧上粉后','画眉后','轻轻偏头','回正 · 保留原嘴','脸颊小猫','脸颊爱心'];
const A=['擦拭','填补划痕','左侧上粉','右侧上粉','画眉','轻轻偏头','回正','画小猫','画爱心'];
const labels=Object.fromEntries(M.states.map((s,i)=>[s.id,S[i]]));
const $=id=>document.getElementById(id), ctx=$('scene').getContext('2d');ctx.imageSmoothingEnabled=false;
let images={}, state=M.states[0].id, mode='idle', frame=0, elapsed=0, paused=false, action=null,lastAction=M.actions[0],queue=[],waitAfter=0,last=0,ready=false;
const pick=$('states');M.states.forEach((s,i)=>pick.add(new Option(S[i],s.id)));
M.actions.forEach((a,i)=>{let b=document.createElement('button');b.textContent=A[i];b.dataset.action=a.id;b.onclick=()=>{queue=[];start(a)};$('actions').append(b)});
function frames(){return mode==='action'?action.frames:stateMap[state].frames}
function draw(){ctx.clearRect(0,0,M.canvas.width,M.canvas.height);ctx.drawImage(images[M.base],0,0);ctx.drawImage(images[stateMap[state].patch],0,0);ctx.drawImage(images[frames()[frame].delta],0,0)}
function ui(){const busy=mode==='action';$('status').textContent=busy?'操作 · '+A[M.actions.indexOf(action)]:'待机 · '+labels[state];$('status').dataset.state=state;$('status').dataset.mode=mode;pick.value=state;$('pause').textContent=paused?'继续':'暂停';$('scrub').disabled=!busy;$('scrub').max=frames().length-1;$('scrub').value=frame;$('counter').textContent=busy?`${frame+1} / ${frames().length} 帧`:'待机循环';document.querySelectorAll('[data-action]').forEach(b=>b.classList.toggle('active',busy&&b.dataset.action===action.id))}
function start(a){if(!ready)return;action=a;lastAction=a;state=a.from;mode='action';frame=0;elapsed=0;paused=false;waitAfter=0;draw();ui()}
function idle(id){state=id;mode='idle';frame=0;elapsed=0;action=null;draw();ui()}
function tick(now){let dt=last?Math.min(250,now-last):0;last=now;if(ready&&!paused){dt*=+$('speed').value;
 if(mode==='idle'&&queue.length){waitAfter+=dt;if(waitAfter>=700)start(queue.shift())}
 elapsed+=dt;let fs=frames();while(elapsed>=fs[frame].durationMs){elapsed-=fs[frame].durationMs;frame++;
  if(frame>=fs.length){if(mode==='action'){let destination=action.to;idle(destination);waitAfter=0;break}else frame=0}fs=frames()}
 draw();ui()}requestAnimationFrame(tick)}
pick.onchange=()=>{if(!ready)return;queue=[];paused=false;idle(pick.value)};
$('pause').onclick=()=>{paused=!paused;ui()};$('replay').onclick=()=>{queue=[];start(lastAction)};
$('cancel').onclick=()=>{queue=[];paused=false;idle(state)};
$('all').onclick=()=>{queue=M.actions.slice(1,8);start(M.actions[0])};
$('scrub').oninput=()=>{if(mode!=='action')return;paused=true;queue=[];frame=+$('scrub').value;elapsed=0;draw();ui()};
function fitZoom(){let n=Math.max(1,Math.min(+$('zoom').value,Math.floor($('scene').parentElement.clientWidth/M.canvas.width)));$('zoom').value=String(n);$('scene').style.width=(M.canvas.width*n)+'px'}
$('zoom').onchange=fitZoom;window.addEventListener('resize',fitZoom);fitZoom();
Promise.all(Object.entries(DATA.images).map(async([key,url])=>{let im=new Image();im.src=url;await im.decode();images[key]=im})).then(()=>{ready=true;draw();ui();requestAnimationFrame(tick)}).catch(e=>{$('status').textContent='预览加载失败：'+e.message});
</script></html>'''
target = ROOT / 'Review/animation_review.html'
target.parent.mkdir(exist_ok=True)
target.write_text(html.replace('__DATA__', payload), encoding='utf-8')
print(f'Offline review player: {target} ({target.stat().st_size:,} bytes)')
