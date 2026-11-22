# RC2. Python-зеркало CatalogValidator / DiagnosisRules / OrderFlow. НЕ является запуском C#-кода.
import json, sys, glob, os
import os as _o
W=_o.environ.get('CD_ROOT','/home/user/workspace/CarDiagnosis_RC2_Work')
from tire_mirror import tire_validate, tire_scenarios
FONT=set(chr(i) for i in range(32,127))|set(chr(i) for i in range(0x410,0x450))|set("Ёё—·№«»")
L=dict(title=48,complaint=220,label=34,head=40,text=220,intro=320)
def glyph(s): return ''.join(sorted(set(ch for ch in s if ch not in FONT and ord(ch)>=32)))
def validate(c, scene=("battery","alternator","engine","brakes")):
    e=[]
    if not isinstance(c,dict) or not c: return ["empty"]
    if c.get("formatVersion")!=1: e.append("formatVersion")
    def T(p,v,m,req=True):
        if not v or not str(v).strip():
            if req: e.append(p+": пустой текст")
            return
        if len(v)>m: e.append(f"{p}: длина {len(v)}>{m}")
        g=glyph(v)
        if g: e.append(f"{p}: нет глифов {g!r}")
    s=c.get("settings") or {}
    if not s: e.append("settings")
    for k,m,r in [("gameTitle",48,1),("subtitle",48,0),("introTitle",48,1),("introText",320,1),("controlsText",90,1),("disclaimer",90,0),("finalTitle",48,1),("finalText",220,0)]: T("settings."+k,s.get(k),m,r)
    def ids(name):
        st=set()
        for i,x in enumerate(c.get(name) or []):
            v=(x or {}).get("id")
            if not v: e.append(f"{name}[{i}] пустой id")
            elif v in st: e.append(f"{name}: дубль {v}")
            else: st.add(v)
        if not c.get(name): e.append(f"{name} пуст")
        return st
    nodes=ids("nodes"); checks=ids("checks"); repairs=ids("repairs"); faults=ids("faults"); ids("orders")
    for n in c.get("nodes") or []:
        T("node.name",n.get("name"),48)
        if n.get("id") not in scene: e.append("узел не в сцене "+str(n.get("id")))
    if len(c.get("checks") or [])>3: e.append("checks>3")
    if len(c.get("repairs") or [])>5: e.append("repairs>5")
    if len(c.get("checks") or [])+len(c.get("repairs") or [])>7: e.append("actions>7")
    if len(c.get("orders") or [])>8: e.append("orders>8")
    for k in c.get("checks") or []:
        for f,m,r in [("label",34,1),("windowTitle",48,1),("hint",220,0),("neutralHeadline",40,1),("neutralText",220,1)]: T("check."+f,k.get(f),m,r)
        if not 1<=k.get("cost",0)<=120: e.append("check cost "+k["id"])
        for n in k.get("nodeIds") or []:
            if n not in nodes: e.append("check node ref "+n)
    for r in c.get("repairs") or []:
        T("repair.label",r.get("label"),34); T("repair.resultName",r.get("resultName"),48)
        if not 1<=r.get("cost",0)<=120: e.append("repair cost "+r["id"])
        if not r.get("nodeIds"): e.append("repair no nodes")
        for n in r.get("nodeIds") or []:
            if n not in nodes: e.append("repair node ref "+n)
    def OBS(lst,fault):
        seen=set()
        for o in lst or []:
            if o.get("checkId") not in checks: e.append("obs check ref")
            if o.get("nodeId") not in nodes: e.append("obs node ref")
            k=next((x for x in c["checks"] if x["id"]==o.get("checkId")),None)
            if k and k.get("nodeIds") and o.get("nodeId") not in k["nodeIds"]: e.append("obs check not on node")
            key=(o.get("checkId"),o.get("nodeId"))
            if key in seen: e.append("obs dup")
            seen.add(key)
            T("obs.headline",o.get("headline"),40); T("obs.text",o.get("text"),220)
            T("obs.fh",o.get("fixedHeadline"),40,False); T("obs.ft",o.get("fixedText"),220,False)
            if fault and bool(o.get("fixedHeadline"))!=bool(o.get("fixedText")): e.append("fixed pair")
    OBS(c.get("baseline"),False)
    for f in c.get("faults") or []:
        if f.get("fixedBy") not in repairs: e.append("fault fixedBy ref "+str(f.get("fixedBy")))
        if not f.get("observations"): e.append("fault no obs")
        OBS(f.get("observations"),True)
    tire_orders=[o for o in c.get("orders") or [] if o.get("type")=="tire_service"]
    if tire_orders: e+=tire_validate(c,T)
    for o in c.get("orders") or []:
        T("order.title",o.get("title"),48); T("order.complaint",o.get("complaint"),220)
        if o.get("type") not in (None,"","diagnosis","tire_service"): e.append("unknown type"); continue
        if o.get("goal") and len(o["goal"])>160: e.append("goal>160")
        if o.get("type")=="tire_service":
            from tire_mirror import minimal_total
            if o.get("faultId"): e.append("tire faultId")
            if c.get("tireService") and not tire_validate(c,lambda *a,**k:None) and o.get("timeBudget",0)<minimal_total(c["tireService"]): e.append(f"budget {o['id']} < tire minimal")
            if not 1<=o.get("timeBudget",0)<=600: e.append("budget")
            continue
        b=o.get("timeBudget",0)
        if not 1<=b<=600: e.append(f"budget {o.get('id')}={b}")
        f=next((x for x in c.get("faults") or [] if x.get("id")==o.get("faultId")),None)
        if not f: e.append("order fault ref "+str(o.get("faultId"))); continue
        need=min_success(c,f)
        if need<0: e.append("unreachable")
        elif 1<=b<need: e.append(f"budget {o['id']} {b} < {need}")
    return e
def allowed(ids,n,empty_all): return bool(n) and ((empty_all) if not ids else n in ids)
def check_on(c,n): v=[k["cost"] for k in c["checks"] if allowed(k.get("nodeIds"),n,True)]; return min(v) if v else -1
def min_success(c,f):
    r=next((x for x in c.get("repairs") or [] if x.get("id")==f.get("fixedBy")),None)
    if not r: return -1
    v=[r["cost"]+check_on(c,n) for n in r.get("nodeIds") or [] if check_on(c,n)>=0]
    return min(v) if v else -1
class Flow:
    def __init__(s,c): s.c=c; s.phase="Intro"; s.results=[]
    def begin(s): s.results=[dict(att=0,done=False,spent=0) for _ in s.c["orders"]]; s.phase="Order"; s.start(0)
    def start(s,i):
        s.i=i; s.left=s.c["orders"][i]["timeBudget"]; s.spent=0; s.rep=0; s.fixed=False; s.any=False; s.cs=False; s.lr=None; s.ln=None; s.out=None
        s.results[i]["att"]+=1
    @property
    def fault(s): return next(f for f in s.c["faults"] if f["id"]==s.c["orders"][s.i]["faultId"])
    def active(s): return s.phase=="Order" and s.out is None
    def chk(s,cid,n):
        k=next(x for x in s.c["checks"] if x["id"]==cid)
        if not(s.active() and allowed(k.get("nodeIds"),n,True) and s.left>=k["cost"]): return None
        f=next((o for o in s.fault["observations"] if o["checkId"]==cid and o["nodeId"]==n),None)
        b=next((o for o in s.c["baseline"] if o["checkId"]==cid and o["nodeId"]==n),None)
        if f and not s.fixed: h=f["headline"]
        elif f and f.get("fixedHeadline"): h=f["fixedHeadline"]
        elif b: h=b["headline"]
        else: h=k["neutralHeadline"]
        s.left-=k["cost"]; s.spent+=k["cost"]; s.any=True
        if s.rep>0 and n==s.ln: s.cs=True
        s.after(); return h
    def can_complete(s): return s.active() and s.rep>0 and s.cs
    def repair(s,rid,n):
        r=next(x for x in s.c["repairs"] if x["id"]==rid)
        if not(s.active() and allowed(r.get("nodeIds"),n,False) and s.left>=r["cost"]): return False
        s.left-=r["cost"]; s.spent+=r["cost"]; s.rep+=1; s.lr=rid; s.ln=n
        if s.fault["fixedBy"]==rid: s.fixed=True
        s.cs=False; s.after(); return True
    def after(s):
        mc=min(k["cost"] for k in s.c["checks"]); mr=min(r["cost"] for r in s.c["repairs"])
        need=mc if s.rep>0 else mr+mc
        if s.out is None and not s.can_complete() and s.left<need: s.out="OutOfTime"
    def complete(s):
        if not s.can_complete(): return False
        s.out="Success" if s.fixed else "WrongFix"
        if s.out=="Success": s.results[s.i].update(done=True,spent=s.spent)
        return True
    def retry(s):
        if s.phase!="Order" or s.out is None: return False
        s.start(s.i); return True
    def next(s):
        if s.phase!="Order" or s.out!="Success": return False
        if s.i+1<len(s.c["orders"]): s.start(s.i+1)
        else: s.phase="Final"
        return True
def play(c):
    f=Flow(c); f.begin(); log=[]
    while f.phase=="Order":
        o=c["orders"][f.i]
        if o.get("type")=="tire_service":
            log.append(o["id"]+": шиномонтаж (сценарии ниже)"); f.out="Success"; f.next(); continue
        flt=f.fault; r=next(x for x in c["repairs"] if x["id"]==flt["fixedBy"])
        node=r["nodeIds"][0]
        # диагностика: все наблюдения неисправности
        seen=[f.chk(ob["checkId"],ob["nodeId"]) for ob in flt["observations"]]
        assert f.repair(r["id"],node); h=f.chk(c["checks"][0]["id"],node); assert f.complete() and f.out=="Success",(o["id"],f.out,f.left)
        log.append(f'{o["id"]}: наблюдения {seen}; ремонт {r["id"]}@{node}; контроль -> {h}; потрачено {f.spent}/{o["timeBudget"]}')
        f.next()
    assert f.phase=="Final"; return log
def wrong_paths(c):
    out=[]
    for i,o in enumerate(c["orders"]):
        if o.get("type")=="tire_service":
            out+=tire_scenarios(c,o); continue
        f=Flow(c); f.begin()
        for _ in range(i): f.out="Success"; f.next()
        flt=f.fault
        wrong=[r for r in c["repairs"] if r["id"]!=flt["fixedBy"]][0]
        f.repair(wrong["id"],wrong["nodeIds"][0]); f.chk(c["checks"][0]["id"],wrong["nodeIds"][0]); f.complete()
        assert f.out=="WrongFix",(o["id"],f.out); f.retry(); assert f.left==o["timeBudget"] and f.rep==0 and f.out is None
        out.append(f'{o["id"]}: неверный {wrong["id"]} -> WrongFix, повтор сбросил состояние')
    return out
if __name__=="__main__":
    ok=True
    for p in sorted(glob.glob(W+'/CatalogExamples/**/*.json',recursive=True))+[W+'/Assets/StreamingAssets/CarDiagnosis/orders.json',W+'/Assets/Diagnoz/Resources/CarDiagnosis/DefaultCatalog.json']:
        rel=os.path.relpath(p,W); txt=open(p,encoding='utf-8').read()
        try: c=json.loads(txt) if txt.strip() else None
        except Exception as ex: print(f"[{rel}] повреждён -> fallback ({ex.__class__.__name__})"); continue
        if c is None: print(f"[{rel}] пуст -> fallback"); continue
        e=validate(c)
        if 'invalid' in rel: print(f"[{rel}] ошибок {len(e)}: {e[:4]} -> fallback"); ok&=bool(e); continue
        print(f"[{rel}] ошибок {len(e)} {e}"); ok&=not e
        if not e:
            for l in play(c)+wrong_paths(c): print("   ",l)
    print("ИТОГ:", "OK" if ok else "FAIL")
