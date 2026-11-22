# RC2. Арифметика раскладки UI (холст 1440x810, ScaleWithScreenSize, match 0.5). НЕ рендер Unity.
import math
W,H=1440,810
def tl(x,y,w,h): return (x,y,x+w,y+h)
def tr(x,y,w,h): return (W-x-w,y,W-x,y+h)
def bc(y,w,h): return ((W-w)/2,H-y-h,(W+w)/2,H-y)
def bl(x,y,w,h): return (x,H-y-h,x+w,H-y)
def tc(y,w,h): return ((W-w)/2,y,(W+w)/2,y+h)
def ovl(a,b): return a[0]<b[2] and b[0]<a[2] and a[1]<b[3] and b[1]<a[3]
ok=True
for res in [(1280,720),(1920,1080)]:
    s=math.exp((math.log(res[0]/W)+math.log(res[1]/H))/2); print(f"{res}: масштаб {s:.3f}, холст {res[0]/s:.0f}x{res[1]/s:.0f}")
def node_h(nc,nr): return 214+20+nc*42+4+20+nr*42+8+44+24+36+14
# Режим ходьбы: карточка, подсказка, предмет в руках, управление, всплывающее сообщение.
walk=dict(card=tl(20,20,390,182),prompt=bc(92,560,48),carry=bl(20,20,330,64),controls=bc(14,640,50),toast=tc(20,600,46))
# Режим осмотра: + жалоба слева, панель узла справа.
for nc,nr in [(2,4),(3,4),(2,5)]:
    hh=node_h(nc,nr)
    R=dict(walk,complaint=tl(20,212,390,150),node=tr(20,20,340,hh))
    R.pop('prompt'); R.pop('carry')
    names=list(R); bad=[(a,b) for i,a in enumerate(names) for b in names[i+1:] if ovl(R[a],R[b])]
    inside=all(r[0]>=0 and r[1]>=0 and r[2]<=W and r[3]<=H for r in R.values())
    print(f"осмотр, проверок {nc}, ремонтов {nr}: панель узла {hh} px, пересечения {bad or 'нет'}, в холсте {inside}")
    ok&=inside and not [p for p in bad if 'toast' not in p]
names=list(walk); bad=[(a,b) for i,a in enumerate(names) for b in names[i+1:] if ovl(walk[a],walk[b])]
print("ходьба: пересечения", bad or "нет"); ok&=not bad
# Окна: центр 720x540 и боковое 470x640 (справа, отступ 40). Кнопки снизу.
for side,w,h,bh in [(False,720,540,44),(True,470,640,42)]:
    for n in range(1,7):
        cols=1 if side else (max(1,n) if n<=3 else 2 if n==4 else 3); rows=math.ceil(n/cols)
        top=h-30-rows*bh-(rows-1)*10; body_top=124 if side else 140; body=max(60,top-body_top-14)
        bw=(w-60-(cols-1)*12)/cols
        good=top>=body_top+60 and bw>=140
        if not good or n in(1,6): print(f"окно {'бок' if side else 'центр'}, кнопок {n}: ширина кнопки {bw:.0f}, текст {body:.0f} px, {'OK' if good else 'ТЕСНО'}")
        ok&=good
    card=(W-40-w,(H-h)/2,W-40,(H+h)/2) if side else ((W-w)/2,(H-h)/2,(W+w)/2,(H+h)/2)
    print(f"  окно {'бок' if side else 'центр'} перекрывает карточку заказа: {ovl(card,walk['card'])}; в холсте {card[1]>=0 and card[3]<=H}")
# Боковое окно у станка оставляет видимой левую часть экрана (станок и колесо в центре-слева).
print(f"боковое окно занимает {470/W*100:.0f}% ширины справа")
print("ИТОГ:", "OK" if ok else "FAIL")
