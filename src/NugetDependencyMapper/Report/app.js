'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const data = JSON.parse($('report-data').textContent);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const lower = value => String(value).toLowerCase();
  const projectIndex = new Map(data.projects.map(p => [p.id, p]));
  const packageIndex = new Map(data.packages.map(p => [lower(p.id), p]));
  const drift = data.packages.filter(p => p.hasVersionDrift);
  const state = {view:'map', explorer:'projects', project:data.projects[0]?.id, target:0, package:null, depth:3, x:0, y:0, scale:1};
  const project = () => projectIndex.get(state.project);
  const target = () => project()?.targets[state.target];
  const selectedPackage = () => packageIndex.get(lower(state.package ?? ''));
  const unique = values => [...new Set(values)];
  const pkgForKey = key => target()?.packages.find(p => p.key === key);
  const labelFor = key => pkgForKey(key)?.id ?? target()?.projectLibraries[key] ?? key;
  const licenseEntries = pkg => pkg.licenses?.length ? pkg.licenses : [{version:'Unresolved',license:{kind:'unknown',note:'Restore the package to read its license metadata.'}}];
  const licenseLabel = license => license.kind==='expression'?license.value:license.kind==='file'?'License file':license.kind==='url'?'Legacy link':'Unknown';
  const licenseBadges = pkg => licenseEntries(pkg).map(({version,license})=>`<div class="license-row"><span class="license-version">${esc(version)}</span>${pill(licenseLabel(license),license.kind==='expression'?'purple':'warning')}${license.requireAcceptance?pill('Acceptance required','warning'):''}</div>`).join('');
  const licenseDetails = pkg => `<section class="inspector-section license-details"><h3>Declared licenses</h3><div class="chain-label">Publisher metadata per resolved version. These labels do not determine permitted use.${data.isDemo?' Demo license data is illustrative.':''}</div>${licenseEntries(pkg).map(({version,license})=>{
    const url=license.url && /^https?:\/\//i.test(license.url)?license.url:null;
    return `<div class="license-detail"><strong>${esc(version)}</strong> ${pill(licenseLabel(license),license.kind==='expression'?'purple':'warning')}${license.kind==='file'?`<div class="chain-label">${esc(license.value)}</div>`:''}${url?`<a href="${esc(url)}" target="_blank" rel="noopener noreferrer">${license.kind==='expression'?'View license expression':'Open license link'} ${icon('arrow-up-right-from-square')}</a>`:''}${license.requireAcceptance?`<div>${pill('Package requires license acceptance','warning')}</div>`:''}${license.note?`<div class="chain-label">${esc(license.note)}</div>`:''}</div>`;
  }).join('')}</section>`;
  const pill = (label, kind='') => `<span class="pill ${kind}">${esc(label)}</span>`;
  const icon = name => `<svg class="icon" aria-hidden="true"><use href="#i-${name}"></use></svg>`;
  const onClick = (id, handler) => $(id).addEventListener('click', handler);
  const initialZoom = {width:800,height:600};
  let graphBounds = {...initialZoom};

  const NS_FILTER_KEY = 'nugetmap-ns-filter';
  function loadExcludePrefixes() {
    try { return (localStorage.getItem(NS_FILTER_KEY) ?? '').split(',').map(s=>s.trim()).filter(Boolean); }
    catch { return []; }
  }
  state.excludePrefixes = loadExcludePrefixes();
  const isExcluded = id => state.excludePrefixes.some(prefix => lower(id).startsWith(lower(prefix)));

  const THEME_KEY = 'nugetmap-theme';
  const systemDark = window.matchMedia('(prefers-color-scheme: dark)');
  const isDarkMode = () => document.documentElement.dataset.theme ? document.documentElement.dataset.theme === 'dark' : systemDark.matches;
  function paintThemeToggle() {
    const dark = isDarkMode();
    $('theme-toggle').setAttribute('aria-pressed', String(dark));
    $('theme-toggle').querySelector('use').setAttribute('href', dark ? '#i-sun' : '#i-moon');
    $('theme-toggle').title = dark ? 'Switch to light mode' : 'Switch to dark mode';
  }
  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'light' || saved === 'dark') document.documentElement.dataset.theme = saved;
  } catch {}
  systemDark.addEventListener('change', () => { if (!document.documentElement.dataset.theme) paintThemeToggle(); });
  onClick('theme-toggle', () => {
    const next = isDarkMode() ? 'light' : 'dark';
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem(THEME_KEY, next); } catch {}
    paintThemeToggle();
  });
  paintThemeToggle();

  $('report-name').textContent = data.name;
  document.title = `${data.name} · NuGet Map`;
  $('generated').textContent = `Generated ${new Date(data.generatedAt).toLocaleString([], {dateStyle:'medium',timeStyle:'short'})}`;
  $('coverage').textContent = `${data.projects.filter(p=>p.resolved).length} / ${data.projects.length} projects have restore data`;
  $('sample-label').hidden = !data.isDemo;
  $('sample-label').title = 'Synthetic package versions and dependency edges for demonstrating the UI; not real package metadata.';
  $('project-count').textContent = data.projects.length;
  $('package-count').textContent = data.packages.length;
  $('drift-count').textContent = drift.length;
  $('drift-badge').textContent = drift.length;
  $('transitive-count').textContent = data.packages.filter(p=>p.usages.some(u=>u.resolved && !u.direct)).length;
  const ERROR_CODES = new Set(['MISSING_PROJECT','INVALID_ASSETS','INVALID_PROJECT','RESTORE_FAILED']);
  if (data.diagnostics.length) {
    const errors = data.diagnostics.filter(d=>ERROR_CODES.has(d.code));
    $('diagnostics-section').hidden = false;
    $('diagnostics-section').classList.toggle('has-errors', errors.length>0);
    $('diagnostics-section').querySelector('details').open = errors.length>0;
    $('diagnostics-title').textContent = errors.length
      ? `${errors.length} error${errors.length===1?'':'s'} to fix · ${data.diagnostics.length} analysis note${data.diagnostics.length===1?'':'s'} total`
      : `${data.diagnostics.length} analysis note${data.diagnostics.length===1?'':'s'} · Check coverage before planning an upgrade`;
    $('diagnostics-list').innerHTML = data.diagnostics.map(d=>`<div class="diagnostic ${ERROR_CODES.has(d.code)?'diagnostic-error':''}"><code>${esc(d.code)}</code>${esc(d.message)}<small>${esc(d.project)}</small></div>`).join('');
  }

  function setView(view) {
    state.view = view;
    document.querySelectorAll('[data-view]').forEach(b=>{b.classList.toggle('active', b.dataset.view===view); b.setAttribute('aria-current', b.dataset.view===view?'page':'false');});
    $('map-view').hidden = view !== 'map';
    $('table-view').hidden = view === 'map';
    if(view !== 'map') renderTable(); else { render(); requestAnimationFrame(fit); }
  }
  function selectProject(id) {
    state.project=id; state.target=0; state.package=null;
    render();
  }
  function selectPackage(id) {
    const pkg=packageIndex.get(lower(id)); if(!pkg)return;
    state.package=pkg.id;
    if(!target()?.packages.some(p=>lower(p.id)===lower(id))) {
      const use=pkg.usages.find(u=>u.project===state.project) ?? pkg.usages[0];
      state.project=use.project;
      state.target=project().targets.findIndex(t=>t.name===use.target);
    }
    render();
  }
  function setExplorer(mode) {
    state.explorer=mode;
    $('projects-tab').classList.toggle('active',mode==='projects');
    $('packages-tab').classList.toggle('active',mode==='packages');
    $('projects-tab').setAttribute('aria-pressed',String(mode==='projects'));
    $('packages-tab').setAttribute('aria-pressed',String(mode==='packages'));
    $('search').placeholder=mode==='projects'?'Find a project…':'Find a package…';
    $('search').value=''; renderExplorer();
  }
  function renderExplorer() {
    const query=lower($('search').value);
    const isProject=state.explorer==='projects';
    const items=(isProject?data.projects:data.packages).filter(p=>lower(isProject?`${p.name} ${p.id}`:p.id).includes(query)).filter(p=>isProject||!isExcluded(p.id));
    $('explorer-count').textContent=items.length;
    $('explorer-list').innerHTML=items.length?items.map(p=>{
      const count=isProject?unique(p.targets.flatMap(t=>t.packages.map(u=>lower(u.id)))).length:unique(p.usages.map(u=>u.project)).length;
      const active=isProject?p.id===state.project:lower(p.id)===lower(state.package??'');
      return `<button class="list-item ${active?'active':''}" data-id="${esc(p.id)}" title="${esc(isProject?p.id:p.id)}" aria-pressed="${active}"><span class="list-glyph">${icon(isProject?'diagram-project':'box')}</span><span class="list-text"><strong>${esc(isProject?p.name:p.id)}</strong><small>${isProject?(p.resolved?`${count} packages · ${p.targets.length} target${p.targets.length===1?'':'s'}`:'Restore data missing'):`${count} project${count===1?'':'s'} · ${p.versions.length?p.versions.length+' version'+(p.versions.length===1?'':'s'):'unresolved'}`}</small></span>${!isProject&&p.hasVersionDrift?'<span class="drift-dot" title="Multiple resolved versions"></span>':`<span class="list-count">${count}</span>`}</button>`;
    }).join(''):'<div class="empty">No matches.<br>Try another search.</div>';
    $('explorer-list').querySelectorAll('[data-id]').forEach(b=>b.addEventListener('click',()=>isProject?selectProject(b.dataset.id):selectPackage(b.dataset.id)));
  }
  function renderTargets() {
    $('target').innerHTML=(project()?.targets??[]).map((t,i)=>`<option value="${i}">${esc(t.name)}</option>`).join('');
    $('target').value=state.target;
    $('target').disabled=!project()?.targets.length;
  }
  function render() { renderExplorer(); renderTargets(); renderGraph(); renderInspector(); }

  function packageProjectRows(pkg) {
    const byProject=new Map();
    for(const u of pkg.usages) { if(!byProject.has(u.project))byProject.set(u.project,[]); byProject.get(u.project).push(u); }
    return [...byProject.entries()].map(([id,uses])=>({
      id, name: projectIndex.get(id)?.name ?? id,
      versions: unique(uses.map(u=>u.key.slice(u.key.lastIndexOf('/')+1))),
      direct: uses.some(u=>u.direct),
    }));
  }

  function renderGraph() {
    const pkg=selectedPackage();
    $('graph-filters').hidden=!!pkg;
    $('export-package').hidden=!pkg;
    if(pkg) renderPackageGraph(pkg); else renderProjectGraph();
  }

  function renderPackageGraph(pkg) {
    const rows=packageProjectRows(pkg);
    $('graph-title').textContent=pkg.id;
    $('graph-subtitle').textContent=`${rows.length} project${rows.length===1?'':'s'} depend on this package`;
    const viewport=$('viewport'); viewport.replaceChildren();
    $('graph-empty').hidden=rows.length>0;
    $('graph-empty').textContent='No projects use this package.';
    if(!rows.length) { $('graph-status').textContent=''; graphBounds={...initialZoom}; requestAnimationFrame(fit); return; }
    const maxRows=rows.length, height=Math.max(350,maxRows*87+85), width=2*245+50;
    graphBounds={width,height};
    const projectHeading=svg('text',{x:30,y:30,class:'graph-level-label','font-size':8,'letter-spacing':1.5});projectHeading.textContent='PROJECTS';viewport.append(projectHeading);
    const packageHeading=svg('text',{x:30+245,y:30,class:'graph-level-label','font-size':8,'letter-spacing':1.5});packageHeading.textContent='PACKAGE';viewport.append(packageHeading);
    const positions=new Map();
    rows.forEach((row,i)=>positions.set(row.id,{x:25,y:65+i*87}));
    const pkgPos={x:25+245,y:65+(maxRows-1)*87/2};
    for(const row of rows) {
      const a=positions.get(row.id),b=pkgPos, delta=Math.max(30,(b.x-a.x-205)/2);
      viewport.append(svg('path',{d:`M${a.x+205},${a.y+29} C${a.x+205+delta},${a.y+29} ${b.x-delta},${b.y+29} ${b.x},${b.y+29}`,class:'graph-edge highlight','marker-end':'url(#arrow)'}));
    }
    for(const row of rows) {
      const pos=positions.get(row.id);
      const group=svg('g',{class:'graph-node',transform:`translate(${pos.x},${pos.y})`,tabindex:0,role:'button','aria-label':`${row.name} · ${row.versions.join(', ')}`});
      const title=svg('title');title.textContent=`${row.name} · ${row.versions.join(', ')}`;group.append(title);
      group.append(svg('rect',{width:205,height:58,rx:8,class:'node-bg node-bg-ref'}));
      group.append(svg('rect',{x:0,y:15,width:3,height:28,rx:1.5,class:'node-accent node-accent-ref'}));
      group.append(svg('use',{href:'#i-diagram-project',x:11,y:15,width:15,height:15,class:'node-icon node-icon-ref'}));
      const text=svg('text',{x:33,y:24,class:'node-text','font-size':10,'font-weight':600});text.textContent=row.name.length>25?row.name.slice(0,24)+'…':row.name;group.append(text);
      const sub=svg('text',{x:33,y:42,class:'node-subtext','font-size':8});sub.textContent=`${row.versions.join(', ')} · ${row.direct?'direct':'transitive'}`;group.append(sub);
      const activate=()=>{if(!dragMoved) selectProject(row.id);};
      group.addEventListener('click',activate);
      group.addEventListener('keydown',event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();activate();}});
      viewport.append(group);
    }
    const pkgGroup=svg('g',{class:'graph-node selected',transform:`translate(${pkgPos.x},${pkgPos.y})`});
    pkgGroup.append(svg('rect',{width:205,height:58,rx:8,class:`node-bg node-bg-package${pkg.hasVersionDrift?' node-bg-drift':''}`}));
    pkgGroup.append(svg('rect',{x:0,y:15,width:3,height:28,rx:1.5,class:`node-accent node-accent-package${pkg.hasVersionDrift?' node-accent-drift':''}`}));
    pkgGroup.append(svg('use',{href:'#i-box',x:11,y:15,width:15,height:15,class:'node-icon node-icon-package'}));
    const pkgName=svg('text',{x:33,y:24,class:'node-text','font-size':10,'font-weight':600});pkgName.textContent=pkg.id.length>25?pkg.id.slice(0,24)+'…':pkg.id;pkgGroup.append(pkgName);
    const pkgSub=svg('text',{x:33,y:42,class:'node-subtext','font-size':8});pkgSub.textContent=pkg.versions.length?pkg.versions.join(', '):'Unresolved';pkgGroup.append(pkgSub);
    if(pkg.hasVersionDrift){const dot=svg('circle',{cx:192,cy:12,r:3,class:'node-drift-dot'});pkgGroup.append(dot);}
    viewport.append(pkgGroup);
    $('graph-status').textContent=`${rows.length} project${rows.length===1?'':'s'} depend on ${pkg.id}`;
    requestAnimationFrame(fit);
  }

  function renderProjectGraph() {
    const p=project(), t=target();
    $('graph-title').textContent=p?.name??'No project selected';
    $('graph-subtitle').textContent=t?`${t.name} · ${t.packages.length} package${t.packages.length===1?'':'s'}`:'No dependency graph available';
    const viewport=$('viewport'); viewport.replaceChildren();
    $('graph-empty').hidden=!!t?.packages.length;
    $('graph-empty').textContent=!p?.resolved?'Restore this project to reveal its dependency graph.':'No NuGet package dependencies in this target.';
    if(!t) { $('graph-status').textContent='No restore data'; return; }
    const allKeys=new Set([...t.packages.filter(u=>!isExcluded(u.id)).map(u=>u.key),...Object.keys(t.projectLibraries)]);
    const levels=new Map([['@project',0]]), queue=[];
    for(const key of t.roots) if(allKeys.has(key)&&!levels.has(key)){levels.set(key,1);queue.push(key);}
    const outgoing=new Map();
    for(const e of t.edges) { if(!outgoing.has(e.from))outgoing.set(e.from,[]);outgoing.get(e.from).push(e.to); }
    for(let i=0;i<queue.length;i++) for(const key of outgoing.get(queue[i])??[]) if(allKeys.has(key)&&!levels.has(key)) { levels.set(key,levels.get(queue[i])+1);queue.push(key); }
    // Keep disconnected restored nodes inspectable without inventing dependency edges.
    for(const key of allKeys) if(!levels.has(key)) levels.set(key,2);
    let nodes=[...levels].filter(([,level])=>level<=state.depth);
    const totalVisible=nodes.length;
    nodes=nodes.slice(0,120);
    const positions=new Map(), columns=new Map();
    for(const [key,level] of nodes){ if(!columns.has(level))columns.set(level,[]);columns.get(level).push(key); }
    const maxRows=Math.max(1,...[...columns.values()].map(c=>c.length));
    const height=Math.max(350,maxRows*87+85), width=(Math.max(0,...columns.keys())+1)*245+50;
    graphBounds={width,height};
    for(const [level,keys] of columns) {
      const heading=svg('text',{x:30+level*245,y:30,class:'graph-level-label','font-size':8,'letter-spacing':1.5});
      heading.textContent=level===0?'PROJECT':level===1?'DIRECT / PROJECT REFERENCES':`DEPENDENCY LEVEL ${level}`;
      viewport.append(heading);
      keys.forEach((key,i)=>positions.set(key,{x:25+level*245,y:65+i*87+(maxRows-keys.length)*87/2}));
    }
    const edges=[...t.roots.map(to=>({from:'@project',to})),...t.edges];
    const selectedKeys=new Set(t.packages.filter(u=>lower(u.id)===lower(state.package??'')).map(u=>u.key));
    for(const edge of edges) {
      const a=positions.get(edge.from),b=positions.get(edge.to);if(!a||!b)continue;
      const back=b.x<=a.x, delta=back?55:Math.max(30,(b.x-a.x-205)/2);
      viewport.append(svg('path',{d:`M${a.x+205},${a.y+29} C${a.x+205+delta},${a.y+29} ${b.x-delta},${b.y+29} ${b.x},${b.y+29}`,
        class:`graph-edge ${selectedKeys.has(edge.from)||selectedKeys.has(edge.to)?'highlight':''}`,'marker-end':'url(#arrow)'}));
    }
    for(const [key] of nodes) {
      const pos=positions.get(key), use=t.packages.find(u=>u.key===key), isProject=key==='@project', isRef=key in t.projectLibraries;
      const drifted=use&&packageIndex.get(lower(use.id))?.hasVersionDrift;
      const name=isProject?p.name:labelFor(key);
      const chosen=isProject?!state.package:selectedKeys.has(key);
      const group=svg('g',{class:`graph-node ${chosen?'selected':''}`,transform:`translate(${pos.x},${pos.y})`,tabindex:0,role:'button','aria-label':`${name}${use?' '+use.version:''}${drifted?', multiple versions across workspace':''}`});
      const title=svg('title');title.textContent=`${name}${use?' · '+use.version:''}${use&&!use.resolved?' (declared, not resolved)':''}${use?' · License: '+licenseLabel(use.license??{kind:'unknown'}):''}`;group.append(title);
      const kind=isProject||isRef?'ref':'package';
      group.append(svg('rect',{width:205,height:58,rx:8,class:`node-bg node-bg-${kind}${isProject?' node-bg-project':''}${drifted?' node-bg-drift':''}`}));
      group.append(svg('rect',{x:0,y:15,width:3,height:28,rx:1.5,class:`node-accent node-accent-${kind}${drifted?' node-accent-drift':''}`}));
      const nodeIcon=svg('use',{href:`#i-${isProject||isRef?'diagram-project':'box'}`,x:11,y:15,width:15,height:15,class:`node-icon node-icon-${kind}`});group.append(nodeIcon);
      const text=svg('text',{x:33,y:24,class:'node-text','font-size':10,'font-weight':600});text.textContent=name.length>25?name.slice(0,24)+'…':name;group.append(text);
      const sub=svg('text',{x:33,y:42,class:'node-subtext','font-size':8});sub.textContent=isProject?'Selected project':isRef?'Referenced project':`${use.version}  ·  ${!use.resolved?'declared':use.direct?'direct':'transitive'}`;group.append(sub);
      if(drifted){const dot=svg('circle',{cx:192,cy:12,r:3,class:'node-drift-dot'});group.append(dot);}
      const activate=()=>{if(isProject){state.package=null;render();}else if(use)selectPackage(use.id);else{const match=data.projects.find(pr=>pr.name===name);if(match)selectProject(match.id);}};
      group.addEventListener('click',()=>{if(!dragMoved)activate();});
      group.addEventListener('keydown',event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();activate();}});
      viewport.append(group);
    }
    const displayed=nodes.length-1, total=allKeys.size;
    $('graph-status').textContent=`${displayed} of ${total} dependencies shown${totalVisible>120?' · 120-node display limit; use inventory to inspect all packages':state.depth<99?' · Adjust depth to explore more':''}${!p.resolved?' · Declarations only':''}`;
    requestAnimationFrame(fit);
  }
  function svg(tag, attributes={}) {const node=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const [key,value] of Object.entries(attributes))node.setAttribute(key,value);return node;}
  function transform(){ $('viewport').setAttribute('transform',`translate(${state.x} ${state.y}) scale(${state.scale})`); }
  function fit(){const rect=$('graph').getBoundingClientRect();if(!rect.width||!rect.height)return;state.scale=Math.min(1.35,(rect.width-30)/graphBounds.width,(rect.height-65)/graphBounds.height);state.x=(rect.width-graphBounds.width*state.scale)/2;state.y=(rect.height-graphBounds.height*state.scale)/2;transform();}
  function zoom(factor,x,y){const rect=$('graph').getBoundingClientRect();x??=rect.width/2;y??=rect.height/2;const scale=Math.min(3,Math.max(.02,state.scale*factor));state.x=x-(x-state.x)*scale/state.scale;state.y=y-(y-state.y)*scale/state.scale;state.scale=scale;transform();}
  let drag=null,dragMoved=false;
  $('graph').addEventListener('pointerdown',e=>{if(e.button!==0)return;dragMoved=false;drag={x:e.clientX,y:e.clientY,ox:state.x,oy:state.y};if(!e.target.closest('.graph-node'))$('graph').setPointerCapture(e.pointerId);});
  $('graph').addEventListener('pointermove',e=>{if(!drag)return;const dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>4)dragMoved=true;if(dragMoved){state.x=drag.ox+dx;state.y=drag.oy+dy;$('graph').classList.add('dragging');transform();}});
  const stopDrag=()=>{drag=null;$('graph').classList.remove('dragging');};
  window.addEventListener('pointerup',stopDrag);$('graph').addEventListener('pointercancel',stopDrag);
  $('graph').addEventListener('wheel',e=>{e.preventDefault();const r=$('graph').getBoundingClientRect();zoom(Math.exp(-e.deltaY*.0015),e.clientX-r.left,e.clientY-r.top);},{passive:false});
  new ResizeObserver(()=>{if(state.view==='map')fit();}).observe($('graph'));

  function pathsTo(key) {
    const t=target(), adjacency=new Map();
    for(const e of t.edges){if(!adjacency.has(e.from))adjacency.set(e.from,[]);adjacency.get(e.from).push(e.to);}
    const queue=t.roots.map(root=>[root]), paths=[];
    let cursor=0, truncated=false;
    while(cursor<queue.length && cursor<4000 && paths.length<12){const path=queue[cursor++], last=path.at(-1);if(last===key){paths.push(path);continue;}if(path.length>=24){truncated=true;continue;}
      for(const next of adjacency.get(last)??[])if(!path.includes(next)){if(queue.length>=4000){truncated=true;break;}queue.push([...path,next]);}
    }
    return {paths,truncated:truncated||cursor<queue.length};
  }
  function renderInspector() {
    const p=project(), t=target(), pkg=selectedPackage();
    if(!p){$('inspector').innerHTML='<div class="empty">Select a project to get started.</div>';return;}
    if(!pkg){
      const uses=t?.packages??[],direct=uses.filter(u=>u.direct),transitive=uses.filter(u=>u.resolved&&!u.direct),drifts=uses.filter(u=>packageIndex.get(lower(u.id))?.hasVersionDrift);
      $('inspector').innerHTML=`<div class="inspector-eyebrow">PROJECT OVERVIEW</div><div class="selection-icon purple">${icon('diagram-project')}</div><h2>${esc(p.name)}</h2><div class="path">${esc(p.id)}</div>${pill(t?.name??'No target','purple')}${pill(p.resolved?'Restored':'Declarations only',p.resolved?'good':'warning')}<div class="mini-stats"><div><strong>${direct.length}</strong><span>Direct packages</span></div><div><strong>${transitive.length}</strong><span>Transitive packages</span></div></div>${!p.resolved?'<div class="notice"><strong>Partial picture</strong>Run with --restore to resolve installed versions and dependency chains.</div>':''}${drifts.length?`<div class="notice"><strong>${icon('arrows-left-right')} ${drifts.length} packages have version drift</strong>Other targets or projects resolve different versions. Review compatibility before aligning them.</div>`:''}<section class="inspector-section"><h3>Direct dependencies <span>${direct.length}</span></h3>${direct.length?direct.map(u=>packageButton(u.id,u.version)).join(''):'<div class="chain-label">No direct PackageReferences in this target.</div>'}</section>${transitive.length?`<section class="inspector-section"><h3>Transitive dependencies <span>${transitive.length}</span></h3>${transitive.map(u=>packageButton(u.id,u.version)).join('')}</section>`:''}${Object.keys(t?.projectLibraries??{}).length?`<section class="inspector-section"><h3>Referenced projects</h3>${Object.values(t.projectLibraries).map(n=>`<div class="chain-label">${icon('diagram-project')} ${esc(n)}</div>`).join('')}</section>`:''}`;
    } else {
      const uses=pkg.usages, projects=unique(uses.map(u=>u.project));
      const current=t.packages.find(u=>lower(u.id)===lower(pkg.id));
      const chains=current?pathsTo(current.key):{paths:[],truncated:false};
      const requirements=uses.filter(u=>u.requested);
      $('inspector').innerHTML=`<div class="inspector-eyebrow">PACKAGE INSPECTOR</div><div class="selection-icon green">${icon('box')}</div><h2>${esc(pkg.id)}</h2><div class="path">${current?esc(current.version)+(current.resolved?' · resolved in this target':' · declared only'):''}</div>${pkg.versions.map(v=>pill(v,pkg.hasVersionDrift?'warning':'good')).join('')}${!pkg.versions.length?pill('Unresolved','warning'):''}<div class="mini-stats"><div><strong>${projects.length}</strong><span>Projects affected</span></div><div><strong>${pkg.versions.length}</strong><span>Resolved versions</span></div></div>${licenseDetails(pkg)}${pkg.hasVersionDrift?`<div class="notice"><strong>${icon('arrows-left-right')} Version drift detected</strong>Versions differ across projects or targets. This can be intentional; verify framework compatibility and breaking changes before upgrading.</div>`:''}<section class="inspector-section"><h3>Used by <span>${projects.length} projects</span></h3>${projects.map(id=>{const pu=uses.filter(u=>u.project===id);return `<button class="detail-row" data-project="${esc(id)}" title="${esc(id)}"><span>${icon('diagram-project')} ${esc(projectIndex.get(id)?.name??id)}</span><span class="tag">${pu.some(u=>u.direct)?'direct':pu.some(u=>u.resolved)?'transitive / inherited':'unclassified'}</span></button><div class="chain-label">${unique(pu.map(u=>u.key.slice(u.key.lastIndexOf('/')+1)+' · '+u.target)).map(esc).join('<br>')}</div>`;}).join('')}</section><section class="inspector-section"><h3>Why is it here?</h3><div class="chain-label">Paths in ${esc(p.name)} · ${esc(t.name)}</div>${chains.paths.map(path=>`<div class="chain"><span>${esc(p.name)}</span>${path.map(key=>`<span>${esc(labelFor(key))}${pkgForKey(key)?' @ '+esc(pkgForKey(key).version):''}</span>`).join('')}</div>`).join('')||'<div class="chain-label">No entry path in this snapshot. Restore data may be incomplete, or the package is supplied implicitly.</div>'}${chains.truncated?'<div class="chain-label">Path display limited to 12 paths, 24 levels and 4,000 explored paths.</div>':''}</section>${current?`<section class="inspector-section"><h3>Depends on</h3>${t.edges.filter(e=>e.from===current.key).map(e=>packageButton(labelFor(e.to),pkgForKey(e.to)?.version??'',e.requested)).join('')||'<div class="chain-label">No further dependencies recorded.</div>'}</section>`:''}${requirements.length?`<section class="inspector-section"><h3>Declared constraints</h3>${requirements.map(u=>`<div class="chain-label">${esc(projectIndex.get(u.project)?.name)} · ${esc(u.target)}<br><b>${esc(u.requested)}</b></div>`).join('')}</section>`:''}`;
    }
    $('inspector').querySelectorAll('[data-package]').forEach(b=>b.addEventListener('click',()=>selectPackage(b.dataset.package)));
    $('inspector').querySelectorAll('[data-project]').forEach(b=>b.addEventListener('click',()=>selectProject(b.dataset.project)));
  }
  function packageButton(id, version, requested=''){return `<button class="detail-row" data-package="${esc(id)}" title="${esc(id)}${requested?' · requested '+esc(requested):''}"><span>${icon('box')} ${esc(id)}</span>${packageIndex.get(lower(id))?.hasVersionDrift?'<i class="drift-dot"></i>':''}<small>${esc(version)}</small></button>`;}
  function renderTable(){
    const isDrift=state.view==='drift',query=lower($('table-search').value),licenseFilter=$('license-filter').value;
    $('table-title').textContent=isDrift?'A shared package. Different versions.':'Every package, in one place.';
    $('table-description').textContent=isDrift?'Prioritize packages with the widest project impact. Different versions are a review signal, not proof of incompatibility. Counts cover all targets.':'Direct and transitive usage across the workspace. Select a package to inspect its projects and dependency chains.';
    const packages=(isDrift?drift:data.packages).filter(p=>!isExcluded(p.id)).filter(p=>lower(p.id+' '+licenseEntries(p).map(l=>l.license.value??'').join(' ')).includes(query)).filter(p=>licenseFilter==='all'||licenseEntries(p).some(({license})=>licenseFilter==='acceptance'?license.requireAcceptance:license.kind===licenseFilter)).sort((a,b)=>unique(b.usages.map(u=>u.project)).length-unique(a.usages.map(u=>u.project)).length||a.id.localeCompare(b.id));
    $('table-empty').hidden=packages.length>0;
    $('table-empty').textContent=isDrift&&!drift.length?'No resolved version drift found. Check analysis notes for any missing restore data.':'No matching packages.';
    $('package-table').innerHTML=packages.map(p=>`<tr><td>${esc(p.id)}${p.hasVersionDrift?`<small>${icon('arrows-left-right')} Version drift</small>`:''}</td><td>${p.versions.map(v=>pill(v,p.hasVersionDrift?'warning':'good')).join('')||pill('Unresolved','warning')}${p.usages.some(u=>!u.resolved)?pill('Includes declarations','warning'):''}</td><td class="license-cell">${licenseBadges(p)}</td><td>${unique(p.usages.map(u=>u.project)).length}</td><td>${p.usages.some(u=>u.direct)?pill('Direct'):''}${p.usages.some(u=>u.resolved&&!u.direct)?pill('Transitive'):''}</td><td><button class="button" data-package="${esc(p.id)}">Inspect ${icon('arrow-up-right-from-square')}</button></td></tr>`).join('');
    $('package-table').querySelectorAll('[data-package]').forEach(b=>b.addEventListener('click',()=>{selectPackage(b.dataset.package);setExplorer('packages');setView('map');}));
  }
  document.querySelectorAll('[data-view]').forEach(b=>b.addEventListener('click',()=>setView(b.dataset.view)));
  onClick('projects-tab',()=>setExplorer('projects'));onClick('packages-tab',()=>setExplorer('packages'));
  onClick('stat-projects',()=>{setExplorer('projects');setView('map');});onClick('stat-packages',()=>setView('inventory'));onClick('stat-drift',()=>setView('drift'));
  $('search').addEventListener('input',renderExplorer);$('table-search').addEventListener('input',renderTable);$('license-filter').addEventListener('change',renderTable);
  $('target').addEventListener('change',()=>{state.target=Number($('target').value);if(!target()?.packages.some(p=>lower(p.id)===lower(state.package??'')))state.package=null;render();});
  $('depth').addEventListener('change',()=>{state.depth=Number($('depth').value);renderGraph();});
  onClick('reset',fit);onClick('zoom-in',()=>zoom(1.25));onClick('zoom-out',()=>zoom(.8));
  onClick('export',()=>{const url=URL.createObjectURL(new Blob([JSON.stringify(data,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='nuget-map.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
  const csvField=value=>{value=String(value??'');return /[",\r\n]/.test(value)?'"'+value.replace(/"/g,'""')+'"':value;};
  onClick('export-package',()=>{
    const pkg=selectedPackage();if(!pkg)return;
    const rows=packageProjectRows(pkg);
    const csv=['Project,Version'].concat(rows.map(r=>`${csvField(r.name)},${csvField(r.versions.join('; '))}`)).join('\r\n');
    const url=URL.createObjectURL(new Blob([csv],{type:'text/csv'}));
    const a=document.createElement('a');a.href=url;a.download=`${pkg.id.replace(/[^a-z0-9.-]+/gi,'_')}-projects.csv`;a.click();
    setTimeout(()=>URL.revokeObjectURL(url),1000);
  });
  function applyNamespaceFilter() {
    const raw=$('ns-filter').value;
    state.excludePrefixes=raw.split(',').map(s=>s.trim()).filter(Boolean);
    try { localStorage.setItem(NS_FILTER_KEY, raw); } catch {}
    const hiddenCount=data.packages.filter(p=>isExcluded(p.id)).length;
    $('ns-filter-count').textContent=hiddenCount?`${hiddenCount} hidden`:'';
    render();
    if(state.view!=='map') renderTable();
  }
  try { $('ns-filter').value=localStorage.getItem(NS_FILTER_KEY) ?? ''; } catch {}
  $('ns-filter-count').textContent=(()=>{const n=data.packages.filter(p=>isExcluded(p.id)).length;return n?`${n} hidden`:'';})();
  $('ns-filter').addEventListener('input', applyNamespaceFilter);
  document.addEventListener('keydown',e=>{if(e.key==='/'&&!e.ctrlKey&&!e.metaKey&&!/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)){e.preventDefault();(state.view==='map'?$('search'):$('table-search')).focus();}});
  setExplorer('projects');render();
})();
