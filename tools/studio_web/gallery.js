"use strict";
const data=JSON.parse(document.getElementById("gallery-data").textContent);
const $=id=>document.getElementById(id), assets=data.assets, runs=data.runs, nodes=new Map(data.nodes.map(n=>[n.id,n]));
const labels={complete:"Superata",failed:"Fallita",cancelled:"Annullata",interrupted:"Interrotta",queued:"In coda",running:"In corso",waiting:"In attesa",cancelling:"Annullamento",unverified:"Senza verifica"};
let selected=assets.find(a=>a.type==="room")||assets[0];
function el(tag,text,cls){const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;}
function empty(node){node.replaceChildren();}
function badge(node,state){node.textContent=labels[state]||state||"Nessuna prova";node.className="badge"+(state==="complete"?" pass":state==="failed"?" fail":"");}
function available(asset){return asset.runs.map(k=>runs[k]);}
function describe(run){return run.state==="unverified"?run.id:`${run.id.slice(0,8)} ${run.id.slice(9,15)} · ${labels[run.state]||run.state}`;}
function filtered(){const q=$("search").value.toLocaleLowerCase(),kind=$("kind").value,state=$("state").value;
 return assets.filter(a=>{const history=available(a);return (kind==="all"||a.type===kind)&&`${a.name} ${a.id} ${a.category} ${a.family} ${a.tags.join(" ")}`.toLocaleLowerCase().includes(q)&&
 (state==="all"||(state==="unverified"?!history.some(r=>r.state==="complete"):history.some(r=>r.state===state)));});}
function list(){const items=filtered();$("detail").hidden=!items.length;
 if(items.length&&!items.some(a=>a.id===selected?.id)){selected=items[0];detail();}
 empty($("assets"));$("count").textContent=`${items.length} di ${assets.length} contenuti`;$("no-results").hidden=items.length>0;
 for(const a of items){const b=el("button",undefined,"asset-button");b.type="button";b.setAttribute("aria-current",String(a.id===selected?.id));
 b.append(el("strong",a.name),el("small",`${a.category} · ${available(a).filter(r=>r.state!=="unverified").length} lavori`));b.onclick=()=>{selected=a;list();detail();};$("assets").append(b);}}
function options(select,history){empty(select);for(const run of history){const option=el("option",describe(run));option.value=run.key;select.append(option);}}
function detail(){if(!selected)return;$("asset-name").textContent=selected.name;$("asset-id").textContent=selected.id;$("category").textContent=selected.category+(selected.family?` / ${selected.family}`:"");empty($("stats"));
 for(const metric of selected.stats){const n=el("div",undefined,"stat");n.append(el("span",metric.label),el("strong",metric.value));$("stats").append(n);}
 const history=available(selected);options($("before"),history);options($("after"),history);
 const preferred=history.find(r=>r.state==="complete"&&r.images[selected.id])||history.find(r=>r.images[selected.id])||history[0];
 if(preferred){const comparable=history.find(r=>r.key!==preferred.key&&Object.keys(r.images[selected.id]||{}).some(v=>preferred.images[selected.id]?.[v]));
 $("after").value=preferred.key;$("before").value=(comparable||history.find(r=>r.key!==preferred.key&&r.images[selected.id])||preferred).key;}
 $("before").disabled=$("after").disabled=history.length===0;views();dependencies();}
function views(){const a=runs[$("before").value],b=runs[$("after").value],left=a?.images[selected.id]||{},right=b?.images[selected.id]||{};
 const old=$("view").value;empty($("view"));const keys=[...new Set([...Object.keys(left),...Object.keys(right)])];
 for(const key of keys){const option=el("option",(data.views[key]||key)+(!left[key]?" · solo B":!right[key]?" · solo A":""));option.value=key;$("view").append(option);}
 $("view").disabled=!keys.length;const common=keys.find(k=>left[k]&&right[k]);$("view").value=left[old]&&right[old]?old:common||Object.keys(right)[0]||keys[0]||"";paint();}
function frame(side,run){const picture=run?.images[selected.id]?.[$("view").value],img=$("image-"+side),message=$("empty-"+side);img.hidden=!picture;message.hidden=!!picture;
 if(picture){img.src=picture.url;img.alt=`${selected.name} · ${data.views[$("view").value]} · ${run.id}`;$("caption-"+side).textContent=`${picture.width} × ${picture.height} px · ${describe(run)}`;}
 else{img.removeAttribute("src");message.textContent=run?"Questa vista non è disponibile per il lavoro selezionato.":"Nessuna anteprima. Genera una vista con Studio per iniziare il confronto.";$("caption-"+side).textContent=run?describe(run):"Nessuna immagine registrata";}}
function paint(){const a=runs[$("before").value],b=runs[$("after").value];frame("a",a);frame("b",b);
 $("comparison-note").textContent=a&&b&&a.key===b.key?"È selezionata la stessa versione sui due lati. Scegli un altro lavoro per confrontare le modifiche.":"La stessa vista viene applicata ai due lati. Dimensioni e condizioni di ripresa possono differire tra lavori.";results(b);}
function results(run){empty($("metrics"));empty($("failures"));empty($("links"));empty($("source-changes"));badge($("run-status"),run?.state);badge($("asset-status"),run?.state);
 if(run?.state==="complete")for(const id of ["asset-status","run-status"]){if(run.sourceChanges===null){$(id).textContent+=" · sorgenti non verificabili";$(id).classList.remove("pass");}else if(run.sourceChanges.length)$(id).textContent+=" · storica";}
 $("run-origin").textContent=run?`${run.id}${run.subject!==selected.id?` · lavoro su ${run.subject}, immagine di contesto`:""}`:"Non ci sono lavori registrati per questo contenuto.";
 if(!run){$("freshness").textContent="Usa ‘asset preview’ o ‘asset build’ per produrre immagini e prove.";$("reproduce").textContent=`python tools/studio.py asset preview ${selected.id}`;return;}
 const metrics=[...run.metrics];if(run.seconds)metrics.push({label:"Tempo del lavoro",value:`${run.seconds} s`});
 for(const item of metrics){const n=el("div",undefined,"metric");n.append(el("strong",item.value),el("span",item.label));$("metrics").append(n);}
 $("freshness").textContent=run.sourceChanges===null?"Immagine senza corrispondenza verificabile con le sorgenti attuali.":run.sourceChanges.length?`Prova storica: ${run.sourceChanges.length} sorgenti o strumenti sono cambiati da questo lavoro.`:`Le ${run.sourceCount} sorgenti registrate coincidono con quelle controllate durante la raccolta della galleria.`;
 if(run.buildMatches===false)$("failures").append(el("p","La ricetta o la disposizione della build non coincide con quella richiesta.","failure"));
 for(const f of run.findings){const n=el("div",undefined,"failure");n.append(el("strong",f.code||"Controllo fallito"),el("p",String(f.observed??"")),el("p","Atteso: "+String(f.expected??"")));$("failures").append(n);}
 if(run.report){const a=el("a","Apri rapporto completo");a.href=run.report;a.target="_blank";a.rel="noopener";$("links").append(a);}
 for(const log of run.logs){const a=el("a","Registro: "+log.name);a.href=log.url;a.target="_blank";a.rel="noopener";$("links").append(a);}
 $("reproduce").textContent=run.reproduce.length?run.reproduce.map(a=>/\s/.test(a)?JSON.stringify(a):a).join(" "):"Nessun comando registrato per questa immagine.";
 for(const source of (run.sourceChanges||[]).slice(0,30))$("source-changes").append(el("li",source));}
function dependencies(){empty($("dependencies"));empty($("changed-file"));empty($("impacts"));const node=nodes.get(selected.id),visited=new Set(),sources=new Map();
 function visit(id){if(visited.has(id))return;visited.add(id);const n=nodes.get(id);if(!n)return;const group=el("div",undefined,"dependency-group"),ul=el("ul");group.append(el("h4",n.name),ul);
 for(const source of n.sources||[]){ul.append(el("li",source.path+(source.pointer?" #"+source.pointer:"")+" · "+source.reason));sources.set(source.wholeFileKey,source.path+" · file intero");if(source.pointer)sources.set(source.impactKey,source.path+" #"+source.pointer);}
 $("dependencies").append(group);for(const dep of n.dependsOn||[])visit(dep);}
 if(node)visit(node.id);for(const [key,label] of [...sources].sort((a,b)=>a[1].localeCompare(b[1]))){const o=el("option",label);o.value=key;$("changed-file").append(o);}$("impact").disabled=!sources.size;}
function impacts(){empty($("impacts"));const found=data.impacts[$("changed-file").value]||[];
 $("impacts").append(el("p",`${found.length} contenuti o gruppi potenzialmente interessati dalle dipendenze registrate.`));
 for(const item of found){const n=el("div",undefined,"impact-item");n.append(el("strong",item.name),el("small",item.via.map(k=>nodes.get(k).name).join(" → ")));$("impacts").append(n);}}
$("search").oninput=list;$("kind").onchange=list;$("state").onchange=list;$("before").onchange=views;$("after").onchange=views;$("view").onchange=paint;$("impact").onclick=impacts;
$("stamp").textContent="Raccolta locale · "+new Date(data.generatedAt).toLocaleString("it-IT");$("warnings").textContent=data.warnings.length?`${data.warnings.length} rapporti non leggibili: dettagli in manifest.json.`:"";
list();detail();
