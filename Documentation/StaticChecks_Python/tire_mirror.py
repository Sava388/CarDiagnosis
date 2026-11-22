# RC2. Python-зеркало TireRules/TireJob (Scripts/Core/TireJob.cs). НЕ запуск C#-кода, только сверка логики.
COST=dict(Remove='removeCost',Dismount='dismountCost',Mount='mountCost',Spin='spinCost',ApplyWeight='weightCost',Install='installCost',Control='controlCost')
def new_job(): return dict(place='Car',ever=False,off=False,new=False,meas=False,bal=False,ctrl=False,inst=True,w=0,read=-1)
def serviced(j): return j['new'] and j['bal']
def cost(d,a): return d[COST[a]] if a in COST else 0
def refusal(j,d,a,g=0):
    p=j['place']
    if a=='Remove': return 'снято' if p!='Car' else ('проверено' if j['ctrl'] and serviced(j) else None)
    if a in('PlaceOnChanger','PlaceOnBalancer'):
        if p!='Hands': return 'нет в руках'
        if a=='PlaceOnBalancer' and not j['new']: return 'сначала шина'
        if a=='PlaceOnChanger' and serviced(j): return 'уже'
        return None
    if a=='TakeFromChanger': return None if p=='Changer' else 'нет'
    if a=='TakeFromBalancer': return None if p=='Balancer' else 'нет'
    if a=='Dismount':
        if p!='Changer': return 'не на станке'
        return 'уже' if j['off'] or j['new'] else None
    if a=='Mount':
        if p!='Changer': return 'не на станке'
        if j['new']: return 'уже'
        return None if j['off'] else 'сначала демонтаж'
    if a=='Spin':
        if p!='Balancer': return 'не на балансировке'
        return 'уже' if j['bal'] else None
    if a=='ApplyWeight':
        if p!='Balancer': return 'не на балансировке'
        if j['bal']: return 'уже'
        if not j['meas']: return 'сначала замер'
        if j['w']>0: return 'груз стоит'
        return None if g in d['weightOptions'] else 'нет груза'
    if a=='Install': return None if p=='Hands' else 'нет в руках'
    if a=='Control':
        if p!='Car' or not j['inst']: return 'не установлено'
        if not j['ever']: return 'не обслуживалось'
        return 'уже' if j['ctrl'] else None
def apply(j,d,a,g=0):
    if a=='Remove': j.update(place='Hands',inst=False,ctrl=False,ever=True)
    elif a=='PlaceOnChanger': j['place']='Changer'
    elif a=='TakeFromChanger': j['place']='Hands'
    elif a=='PlaceOnBalancer': j['place']='Balancer'
    elif a=='TakeFromBalancer': j['place']='Hands'
    elif a=='Dismount': j['off']=True
    elif a=='Mount': j.update(new=True,meas=False,bal=False,w=0,read=-1)
    elif a=='Spin':
        if j['w']==0: j.update(meas=True,read=d['imbalanceGrams'])
        elif j['w']==d['imbalanceGrams']: j.update(bal=True,read=0)
        else: j.update(read=abs(d['imbalanceGrams']-j['w']),w=0)
    elif a=='ApplyWeight': j['w']=g
    elif a=='Install': j.update(place='Car',inst=True,ctrl=False)
    elif a=='Control': j['ctrl']=True
def remaining(j,d):
    n=0
    if not serviced(j):
        if j['place']=='Car': n+=d['removeCost']
        if not j['new']:
            if not j['off']: n+=d['dismountCost']
            n+=d['mountCost']
        if not j['bal']:
            if j['new'] and j['w']==d['imbalanceGrams']: n+=d['spinCost']
            else:
                if not(j['new'] and j['meas']): n+=d['spinCost']
                n+=d['weightCost']+d['spinCost']
        n+=d['installCost']+d['controlCost']
    else:
        if j['place']!='Car': n+=d['installCost']
        if not j['ctrl']: n+=d['controlCost']
    return n
def minimal_total(d): return remaining(new_job(),d)
class TireOrder:
    def __init__(s,d,budget): s.d=d; s.budget=budget; s.restart()
    def restart(s): s.j=new_job(); s.left=s.budget; s.spent=0; s.out=None
    def can_complete(s): return s.out is None and s.j['place']=='Car' and s.j['inst'] and s.j['ctrl']
    def do(s,a,g=0):
        if s.out is not None or refusal(s.j,s.d,a,g) or s.left<cost(s.d,a): return False
        c=cost(s.d,a); s.left-=c; s.spent+=c; apply(s.j,s.d,a,g)
        if s.out is None and not s.can_complete() and s.left<remaining(s.j,s.d): s.out='OutOfTime'
        return True
    def complete(s):
        if not s.can_complete(): return False
        s.out='Success' if serviced(s.j) else 'WrongFix'; return True
FULL=['Remove','PlaceOnChanger','Dismount','Mount','TakeFromChanger','PlaceOnBalancer','Spin','W','Spin','TakeFromBalancer','Install','Control']
def tire_validate(c,T):
    e=[]; t=c.get('tireService')
    if not t: return ['нет tireService']
    for k,m in [('wheelName',40),('fromTire',20),('toTire',20),('imbalanceSide',40)]: T('tireService.'+k,t.get(k),m)
    if t.get('fromTire')==t.get('toTire'): e.append('from==to')
    for k in COST.values():
        if not 1<=t.get(k,0)<=120: e.append('cost '+k)
    w=t.get('weightOptions') or []
    if not 2<=len(w)<=3: e.append('weightOptions count')
    if len(set(w))!=len(w) or any(not 5<=g<=200 for g in w): e.append('weightOptions values')
    if t.get('imbalanceGrams') not in w: e.append('imbalanceGrams not in options')
    return e
def tire_scenarios(c,o):
    d=c['tireService']; out=[]; b=o['timeBudget']
    f=TireOrder(d,b)
    for a in FULL: assert f.do('ApplyWeight',d['imbalanceGrams']) if a=='W' else f.do(a),(a,f.j)
    assert f.complete() and f.out=='Success'; assert f.spent==minimal_total(d)
    out.append(f"{o['id']}: верный путь {f.spent}/{b} мин -> Success (одно колесо)")
    wrong=[g for g in d['weightOptions'] if g!=d['imbalanceGrams']][0]
    f=TireOrder(d,b)
    for a in FULL[:7]: assert f.do(a)
    assert not f.do('ApplyWeight',999); assert f.do('ApplyWeight',wrong); assert not f.do('ApplyWeight',wrong); assert f.do('Spin') and not f.j['bal']
    assert f.do('ApplyWeight',d['imbalanceGrams']) and f.do('Spin') and f.j['bal']
    for a in FULL[9:]: assert f.do(a)
    assert f.complete() and f.out=='Success'; out.append(f"{o['id']}: неверный груз {wrong} г, затем верный -> Success, {f.spent}/{b}")
    f=TireOrder(d,b); assert not f.can_complete(); f.do('Remove'); assert not f.complete() and not f.do('Remove') and not f.do('Control') and not f.do('PlaceOnBalancer')
    f.do('PlaceOnChanger'); assert not f.do('Mount') and not f.do('Spin'); f.do('Dismount'); f.do('TakeFromChanger'); f.restart()
    assert f.j==new_job() and f.left==b; out.append(f"{o['id']}: отказы (повторное снятие, балансировка до шины, монтаж до демонтажа), перезапуск с колесом в руках -> колесо на машине")
    f=TireOrder(d,b); f.do('Remove'); f.do('Install'); f.do('Control'); assert f.complete() and f.out=='WrongFix'; f.restart(); assert f.j['place']=='Car' and not f.j['ever']
    out.append(f"{o['id']}: снять/поставить без обслуживания -> WrongFix, повтор сбросил колесо")
    f=TireOrder(d,b)
    for a in FULL[:7]: f.do(a)
    for _ in range(20):
        if f.out: break
        f.do('ApplyWeight',wrong); f.do('Spin')
    assert f.out=='OutOfTime' and not f.do('Spin'); out.append(f"{o['id']}: перебор грузов -> OutOfTime при остатке {f.left} мин, действия заблокированы")
    # перебор всех коротких последовательностей: колесо всегда в одном месте, Success только при обслуженном колесе на машине
    import itertools
    acts=['Remove','PlaceOnChanger','TakeFromChanger','Dismount','Mount','PlaceOnBalancer','TakeFromBalancer','Spin','Install','Control']
    cnt=0
    def dfs(f,depth):
        nonlocal cnt
        cnt+=1
        assert f.j['place'] in('Car','Hands','Changer','Balancer')
        assert (f.j['place']=='Car')==f.j['inst']
        if f.can_complete():
            import copy; g=copy.deepcopy(f); g.complete(); assert (g.out=='Success')==serviced(g.j)
        if depth==0 or f.out: return
        import copy
        for a in acts+['W'+str(x) for x in d['weightOptions']]:
            g=copy.deepcopy(f)
            ok=g.do('ApplyWeight',int(a[1:])) if a[0]=='W' else g.do(a)
            if ok: dfs(g,depth-1)
    dfs(TireOrder(d,b),13)
    out.append(f"{o['id']}: перебор {cnt} состояний до 13 действий: одно место колеса, Success только при новой шине + балансировке")
    return out
