"""Original procedural sedan study, stdlib only. Not an exact VAZ-2106 replica.
Run ONLY inside this new project's SourceArt. Writes three model files in this project.
Unity uses JSON, OBJ/MTL are portable source interchange files. No downloads.
"""
from pathlib import Path
import math,json
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'Assets/Diagnoz/Art/Vehicle'
MATS={
'Paint':(.13,.30,.32,1),'Chrome':(.65,.70,.72,1),'Rubber':(.035,.04,.045,1),
'Plastic':(.08,.09,.095,1),'Glass':(.16,.26,.29,.45),'Seat':(.16,.10,.075,1),
'Headlight':(.86,.88,.72,1),'Amber':(.95,.35,.04,1),'RedLamp':(.64,.045,.025,1),
'BatteryCase':(.075,.09,.11,1),'BatteryTop':(.19,.22,.23,1),'RedWire':(.75,.045,.03,1),
'BlackWire':(.025,.025,.03,1),'EngineMetal':(.36,.39,.40,1),'Blue':(.06,.23,.42,1),
'Coolant':(.78,.73,.57,1),
'Rim':(.56,.58,.59,1),'Liner':(.03,.032,.035,1),'Plate':(.88,.89,.87,1),'Marking':(.80,.81,.80,1),
'InteriorDark':(.055,.05,.05,1),'BrakeDisc':(.30,.29,.28,1)}
parts=[]
VARIANT=''   # '' = общая деталь; 'summer'/'winter' = сменная шина обслуживаемого колеса
def add(name,group,mat,v,t,collider=True):
 parts.append(dict(name=f'{name}_{len(parts):04d}',group=group,material=mat,variant=VARIANT,vertices=[dict(x=round(x,6),y=round(y,6),z=round(z,6)) for x,y,z in v],triangles=t,collider=collider))
def box(name,c,s,mat,group='Body',rot=0,collider=True):
 x,y,z=c;a,b,d=[i/2 for i in s];r=math.radians(rot)
 v=[]
 for X,Y,Z in [(-a,-b,-d),(a,-b,-d),(a,b,-d),(-a,b,-d),(-a,-b,d),(a,-b,d),(a,b,d),(-a,b,d)]:
  v.append((x+X,y+Y*math.cos(r)-Z*math.sin(r),z+Y*math.sin(r)+Z*math.cos(r)))
 t=[0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2]
 add(name,group,mat,v,t,collider)
def rod(name,a,b,r,mat,group='Body',n=12,collider=True):
 w=[b[i]-a[i] for i in range(3)];length=math.sqrt(sum(x*x for x in w));w=[x/length for x in w]
 temp=(0,1,0) if abs(w[1])<.95 else (1,0,0)
 def cross(u,v):return (u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])
 u=cross(w,temp);l=math.sqrt(sum(x*x for x in u));u=[x/l for x in u];v=cross(w,u)
 verts=[]
 for p in (a,b):
  for j in range(n):
   angle=j*2*math.pi/n;verts.append(tuple(p[i]+r*(u[i]*math.cos(angle)+v[i]*math.sin(angle)) for i in range(3)))
 verts += [a,b];t=[]
 for j in range(n):
  k=(j+1)%n;t.extend([j,n+j,n+k,j,n+k,k,2*n,j,k,2*n+1,n+k,n+j])
 t=[k for i in range(0,len(t),3) for k in (t[i],t[i+2],t[i+1])]
 add(name,group,mat,verts,t,collider)
def quad(name,points,mat,group='Body',collider=True):
 add(name,group,mat,points+list(reversed(points)),[0,1,2,0,2,3,4,5,6,4,6,7],collider)
def wire(name,points,r,mat,group='Body'):
 for a,b in zip(points,points[1:]):rod(name,a,b,r,mat,group,n=8,collider=False)
def revolve(name,c,sign,profile,mat,group,n=40,collider=False):
 # Тело вращения вокруг оси X. profile: замкнутый контур (t,r), t вдоль оси наружу (умножается на sign).
 cx,cy,cz=c;m=len(profile);verts=[];t=[]
 area=sum(profile[i][0]*profile[(i+1)%m][1]-profile[(i+1)%m][0]*profile[i][1] for i in range(m))
 for i in range(n):
  a=2*math.pi*i/n
  for (tt,r) in profile:verts.append((cx+sign*tt,cy+r*math.cos(a),cz+r*math.sin(a)))
 def V(i,j):return verts[(i%n)*m+(j%m)]
 for i in range(n):
  a=2*math.pi*(i+.5)/n;ca,sa=math.cos(a),math.sin(a)
  for j in range(m):
   (t0,r0),(t1,r1)=profile[j],profile[(j+1)%m]
   dt,dr=t1-t0,r1-r0
   nt,nr=(dr,-dt) if area>0 else (-dr,dt)   # наружная нормаль контура
   out=(sign*nt,nr*ca,nr*sa)
   q=[(i%n)*m+j,((i+1)%n)*m+j,((i+1)%n)*m+(j+1)%m,(i%n)*m+(j+1)%m]
   for tri in ((q[0],q[1],q[2]),(q[0],q[2],q[3])):
    A,B,C=verts[tri[0]],verts[tri[1]],verts[tri[2]]
    u=[B[k]-A[k] for k in range(3)];w=[C[k]-A[k] for k in range(3)]
    cr=(u[1]*w[2]-u[2]*w[1],u[2]*w[0]-u[0]*w[2],u[0]*w[1]-u[1]*w[0])
    if sum(cr[k]*out[k] for k in range(3))<0:tri=(tri[0],tri[2],tri[1])
    t.extend(tri)
 add(name,group,mat,verts,t,collider)
def tire_profile(R,w,r0,grooves):
 h=w/2;pts=[(-h+.012,r0),(-h,r0+.03),(-h-.007,r0+.055),(-h-.004,R-.03),(-h+.01,R-.008),(-h+.03,R)]
 for gt in grooves:pts+= [(gt-.007,R),(gt-.006,R-.009),(gt+.006,R-.009),(gt+.007,R)]
 pts+=[(h-.03,R),(h-.01,R-.008),(h+.004,R-.03),(h+.007,r0+.055),(h,r0+.03),(h-.012,r0)]
 return pts
def wheel(sign,z,g,serviced):
 global VARIANT
 c=(sign*.78,.302,z)
 VARIANT='summer'
 revolve('Tire',c,sign,tire_profile(.295,.195,.205,[-.045,0,.045]),'Rubber',g,n=48)
 if serviced:
  VARIANT='winter'
  revolve('WinterTire',c,sign,tire_profile(.281,.195,.205,[]),'Rubber',g,n=48)
  for i in range(30):
   for row,off in ((-1,0),(1,.5)):
    a=2*math.pi*(i+off)/30;r=.288
    box('TreadBlock',(c[0]+row*sign*.046,c[1]+r*math.cos(a),c[2]+r*math.sin(a)),(.078,.016,.044),'Rubber',g,rot=math.degrees(a),collider=False)
  for k in range(3):
   a=2*math.pi*k/3+.5;cy,cz=c[1]+.248*math.cos(a),c[2]+.248*math.sin(a)
   for d in (0,60,120):box('Snowflake',(c[0]+sign*.102,cy,cz),(.004,.005,.05),'Marking',g,rot=d,collider=False)
 VARIANT=''
 # Стальной диск: обод, диск с отверстиями, колпак, гайки (общие для обоих вариантов шины).
 revolve('RimBarrel',c,sign,[(-.085,.198),(.085,.198),(.088,.208),(-.088,.208)],'Rim',g,n=40)
 outer=sign*(.78+.088)
 revolve('RimDish',(outer-sign*.02,c[1],c[2]),sign,[(0,.03),(.02,.06),(.024,.17),(.008,.205),(-.004,.205),(-.004,.03)],'Rim',g,n=40)
 rod('Hub',(outer,.302,z),(outer+sign*.02,.302,z),.075,'Chrome',g,n=24)
 rod('HubCap',(outer+sign*.02,.302,z),(outer+sign*.03,.302,z),.05,'Chrome',g,n=24)
 for i in range(8):
  a=2*math.pi*i/8;yy=.302+.125*math.cos(a);zz=z+.125*math.sin(a)
  rod('RimVent',(outer+sign*.0045,yy,zz),(outer+sign*.0065,yy,zz),.024,'Liner',g,n=10,collider=False)
 for i in range(4):
  a=math.pi/4+math.pi*i/2;yy=.302+.06*math.cos(a);zz=z+.06*math.sin(a)
  rod('Lug',(outer+sign*.02,yy,zz),(outer+sign*.034,yy,zz),.011,'Chrome',g,n=6,collider=False)
# Sill, floor pan and actual open engine bay. No invisible full-car collider.
box('FloorPan',(0,.34,0),(1.28,.09,3.85),'Plastic')
for z in (-1.85,1.85):box('FloorPanEnd',(0,.37,z),(1.46,.06,.16),'Plastic')
for sign in (-1,1):
 x=sign*.79
 box('Rocker',(x,.38,0),(.08,.15,1.66),'Liner')
 for z in (-1.79,1.79):box('RockerEnd',(x,.42,z),(.06,.07,.38),'Paint')
 # Strip mesh with wheel-arch cutouts, rather than a box intersecting tires.
 zs=sorted(set([-2.0,-1.90,-1.65,-1.212-.34,-1.212,-1.212+.34,-.6,0,.6,1.212-.34,1.212,1.212+.34,1.9,2.0]+[round(c+.345*math.cos(i*math.pi/24),6) for c in (-1.212,1.212) for i in range(25)]))
 def bottom(z):
  y=.40
  for c in (-1.212,1.212):
   if abs(z-c)<.345:y=max(y,.32+math.sqrt(max(0,.345**2-(z-c)**2)))
  return y
 for a,b in zip(zs,zs[1:]):
  quad('FenderSide',[(x,bottom(a),a),(x,bottom(b),b),(x,.94,b),(x,.94,a)],'Paint')
 for c in (-1.212,1.212):
  pts=[(sign*.803,.32+.351*math.sin(i*math.pi/24),c+.351*math.cos(i*math.pi/24)) for i in range(25)]
  wire('ArchLip',pts,.012,'Chrome')
 box('FrontFenderTop',(sign*.70,.95,-1.22),(.21,.055,1.53),'Paint')
 box('Shoulder',(sign*.75,.955,.52),(.12,.055,2.45),'Paint')
 box('WaistChrome',(sign*.802,.88,0),(.013,.022,3.75),'Chrome')
 box('LowerTrim',(sign*.81,.46,0),(.012,.018,1.62),'Chrome')
 # Door seams and handles: two doors on each side.
 for z in (-.48,.40,1.33):rod('DoorSeam',(sign*.797,.46,z),(sign*.797,.93,z),.004,'Plastic',collider=False)
 for z in (.12,1.02):
  box('HandleBase',(sign*.813,.845,z),(.026,.055,.19),'Plastic')
  box('DoorHandle',(sign*.835,.86,z),(.035,.025,.15),'Chrome')
 # Колёса: переднее левое (front = -z, left = -x) обслуживается и имеет вариант зимней шины.
 for z in (-1.212,1.212):
  g=('Wheel_L_' if sign<0 else 'Wheel_R_')+('Front' if z<0 else 'Rear')
  wheel(sign,z,g,serviced=(sign<0 and z<0))
  # Тормозной диск и ступица остаются на машине, когда колесо снято.
  rod('BrakeDisc',(sign*.655,.302,z),(sign*.675,.302,z),.13,'BrakeDisc',n=32,collider=False)
  rod('HubFlange',(sign*.675,.302,z),(sign*.69,.302,z),.06,'EngineMetal',n=20,collider=False)
  box('Caliper',(sign*.67,.302+.09,z+.09*(-1 if z<0 else 1)),(.05,.07,.09),'Plastic',collider=False)
  # Подкрылок: тёмная арка внутри колёсной ниши.
  for i in range(24):
   a0,a1=math.pi*i/24,math.pi*(i+1)/24
   p0=(.32+.36*math.sin(a0),z+.36*math.cos(a0));p1=(.32+.36*math.sin(a1),z+.36*math.cos(a1))
   quad('ArchLiner',[(sign*.60,p0[0],p0[1]),(sign*.60,p1[0],p1[1]),(sign*.79,p1[0],p1[1]),(sign*.79,p0[0],p0[1])],'Liner',collider=False)
# Front and rear structure, lamps, bumpers.
box('FrontPanel',(0,.66,-1.96),(1.60,.45,.08),'Paint')
box('GrilleRecess',(0,.755,-2.012),(1.46,.25,.035),'Plastic')
for y in (.66,.70,.74,.78,.82,.86):box('GrilleBar',(0,y,-2.036),(.62,.012,.014),'Chrome')
for x in (-.18,0,.18):box('GrilleUpright',(x,.76,-2.044),(.012,.21,.01),'Chrome')
for x in (-.61,-.38,.38,.61):
 rod('LampBezel',(x,.76,-2.04),(x,.76,-2.075),.108,'Chrome',n=24)
 rod('RoundHeadlight',(x,.76,-2.079),(x,.76,-2.085),.086,'Headlight',n=24)
 for dx in (-.028,0,.028):rod('LensRib',(x+dx,.705,-2.088),(x+dx,.815,-2.088),.002,'Chrome',n=6,collider=False)
for x in (-.65,.65):
 box('FrontSignal',(x,.525,-2.015),(.21,.075,.022),'Amber')
 box('RearLampBlack',(x,.77,2.015),(.30,.17,.025),'Plastic')
 box('RearRed',(x-.035,.78,2.037),(.19,.11,.015),'RedLamp')
 box('RearIndicator',(x+.10,.78,2.039),(.065,.11,.016),'Amber')
box('RearPanel',(0,.68,1.97),(1.58,.45,.08),'Paint')
box('Trunk',(0,.96,1.50),(1.48,.06,.92),'Paint')
for x in (-.73,.73):rod('TrunkSeam',(x,.992,1.12),(x,.992,1.93),.003,'Plastic',collider=False)
for z in (-2.07,2.07):
 box('Bumper',(0,.43,z),(1.72,.105,.12),'Chrome')
 box('BumperRubber',(0,.44,z+(-.066 if z<0 else .066)),(1.66,.032,.025),'Rubber')
 for x in (-.55,.55):box('BumperGuard',(x,.44,z),(.05,.19,.16),'Rubber')
box('PlateRecessFront',(0,.53,-2.018),(.36,.08,.013),'Plastic')
box('PlateRecessRear',(0,.68,2.018),(.37,.12,.013),'Plastic')
box('PlateFront',(0,.53,-2.027),(.33,.068,.006),'Plate',collider=False)
box('PlateRear',(0,.68,2.027),(.34,.10,.006),'Plate',collider=False)
rod('Exhaust',(.45,.30,1.80),(.45,.30,2.11),.028,'EngineMetal',n=14,collider=False)
rod('ExhaustTip',(.45,.30,2.11),(.45,.30,2.13),.022,'Liner',n=14,collider=False)
# Cabin trapezoid: windshield slope, roof, side glazing and chrome frames.
box('Roof',(0,1.435,.43),(1.23,.06,1.30),'Paint')
front=[(-.72,.995,-.56),(.72,.995,-.56),(.60,1.405,-.20),(-.60,1.405,-.20)]
rear=[(-.60,1.405,1.07),(.60,1.405,1.07),(.72,.995,1.23),(-.72,.995,1.23)]
for nm,pts in [('Windshield',front),('RearGlass',rear)]:
 quad(nm,pts,'Glass','Glass')
 wire(nm+'Seal',pts+[pts[0]],.022,'Rubber')
 wire(nm+'Chrome',pts+[pts[0]],.010,'Chrome')
for sign in (-1,1):
 a=(sign*.72,.995,-.56);b=(sign*.60,1.405,-.20);c=(sign*.60,1.405,1.07);d=(sign*.72,.995,1.23)
 wire('Pillars',[a,b,c,d],.03,'Paint')
 quad('FrontDoorWindow',[a,(sign*.715,1,.36),(sign*.60,1.40,.36),b],'Glass','Glass')
 quad('RearDoorWindow',[(sign*.715,1,.41),d,c,(sign*.60,1.40,.41)],'Glass','Glass')
 rod('BPillar',(sign*.715,.98,.385),(sign*.60,1.425,.385),.025,'Paint')
 wire('WindowTrim',[a,b,c,d,a],.009,'Chrome')
 rod('QuarterLight',(sign*.715,1,-.31),(sign*.63,1.31,-.31),.008,'Chrome')
 rod('MirrorArm',(sign*.75,1.01,-.42),(sign*.91,1.04,-.48),.012,'Chrome')
 box('MirrorBody',(sign*.94,1.07,-.48),(.13,.10,.065),'Chrome')
 box('MirrorGlass',(sign*.94,1.07,-.445),(.105,.075,.005),'Glass','Glass')
# Wipers along lower windscreen.
for x in (-.32,.32):
 rod('WiperArm',(x,.999,-.565),(x+.15,1.055,-.51),.007,'Plastic')
 rod('WiperBlade',(x-.04,1.06,-.509),(x+.24,1.06,-.509),.009,'Rubber')
# Simplified interior, steering wheel, instrument binnacle. Тёмная обивка: за стёклами не видно «пустоты».
box('Headliner',(0,1.395,.43),(1.20,.018,1.26),'InteriorDark','Interior',collider=False)
box('Carpet',(0,.40,.45),(1.40,.02,1.65),'InteriorDark','Interior',collider=False)
for sign in (-1,1):
 quad('DoorCard',[(sign*.70,.48,-.50),(sign*.70,.48,1.22),(sign*.70,.98,1.22),(sign*.70,.98,-.50)],'InteriorDark','Interior',collider=False)
box('RearShelf',(0,.99,1.22),(1.30,.02,.2),'InteriorDark','Interior',collider=False)
box('Dashboard',(0,.91,-.42),(1.35,.13,.21),'Plastic','Interior')
for x in (-.35,.35):
 box('FrontSeatBase',(x,.56,.20),(.48,.17,.47),'Seat','Interior')
 box('FrontSeatBack',(x,.82,.45),(.48,.49,.12),'Seat','Interior',rot=-10)
 box('Headrest',(x,1.10,.48),(.28,.13,.11),'Seat','Interior')
box('RearSeatBase',(0,.60,.99),(1.20,.19,.38),'Seat','Interior')
box('RearSeatBack',(0,.83,1.12),(1.18,.39,.12),'Seat','Interior')
box('InstrumentCluster',(-.36,1.0,-.43),(.34,.15,.10),'Plastic','Interior')
for x in (-.44,-.30):rod('Dial',(x,1.015,-.368),(x,1.015,-.362),.048,'Chrome','Interior',n=16)
rod('SteeringColumn',(-.36,.82,-.38),(-.36,.96,-.18),.025,'Plastic','Interior')
wheel=[(-.36+.145*math.cos(i*math.pi/16),.99+.145*math.sin(i*math.pi/16),-.15) for i in range(33)]
wire('SteeringRim',wheel,.012,'Plastic','Interior')
for i in range(3):
 a=i*2*math.pi/3;rod('SteeringSpoke',(-.36,.99,-.15),(-.36+.14*math.cos(a),.99+.14*math.sin(a),-.15),.008,'Chrome','Interior')
rod('GearLever',(0,.43,.12),(0,.71,.07),.013,'Chrome','Interior');box('GearKnob',(0,.73,.07),(.055,.05,.05),'Plastic','Interior')
# Engine compartment. Battery priority: visible near-front corner, separate stable node group.
box('BayFloor',(0,.53,-1.24),(1.22,.07,1.38),'Plastic')
box('Firewall',(0,.82,-.48),(1.35,.55,.06),'Paint')
for s in (-1,1):box('InnerWing',(s*.59,.75,-1.21),(.06,.4,1.37),'Paint')
box('Radiator',(0,.80,-1.85),(.92,.35,.08),'Plastic')
for x in [i*.055 for i in range(-8,9)]:box('RadiatorFin',(x,.80,-1.90),(.008,.31,.01),'EngineMetal')
box('EngineBlock',(0,.75,-1.07),(.42,.40,.66),'EngineMetal','Node_Engine')
box('ValveCover',(0,1.0,-1.07),(.31,.11,.59),'EngineMetal','Node_Engine')
rod('AirFilter',(0,1.07,-1.05),(0,1.15,-1.05),.19,'Plastic','Node_Engine',n=24)
for z in (-1.29,-1.16,-1.03,-.90):wire('IgnitionLead',[(.12,1.03,z),(.22,1.12,z),(.27,.90,z)],.009,'BlackWire','Node_Engine')
rod('Alternator',(.30,.72,-1.52),(.30,.72,-1.34),.115,'EngineMetal','Node_Alternator',n=16)
rod('AlternatorPulley',(.30,.72,-1.55),(.30,.72,-1.53),.08,'Plastic','Node_Alternator',n=16)
wire('DriveBelt',[(.30,.80,-1.56),(.02,.68,-1.56),(.30,.64,-1.56),(.30,.80,-1.56)],.012,'Rubber')
box('BatteryTray',(-.405,.79,-1.49),(.32,.05,.44),'EngineMetal')
box('BatteryBody',(-.405,.925,-1.49),(.29,.235,.38),'BatteryCase','Node_Battery')
box('BatteryLid',(-.405,1.055,-1.49),(.305,.035,.395),'BatteryTop','Node_Battery')
box('BatteryLabel',(-.405,.946,-1.686),(.20,.09,.003),'Blue','Node_Battery',collider=False)
for z in (-1.61,-1.37):
 rod('Terminal',(-.405,1.072,z),(-.405,1.11,z),.022,'Chrome','Node_Battery',n=12)
 box('TerminalClamp',(-.405,1.105,z),(.065,.02,.055),'Chrome','Node_Battery')
box('PositiveCap',(-.405,1.129,-1.61),(.055,.02,.05),'RedWire','Node_Battery')
box('NegativeCap',(-.405,1.129,-1.37),(.055,.02,.05),'BlackWire','Node_Battery')
wire('PositiveCable',[(-.405,1.12,-1.61),(-.54,1.15,-1.69),(-.55,.86,-1.76),(-.15,.67,-1.70)],.012,'RedWire','Node_Battery')
wire('EarthCable',[(-.405,1.12,-1.37),(-.55,1.13,-1.30),(-.58,.89,-1.18)],.012,'BlackWire','Node_Battery')
for z in (-1.57,-1.49,-1.41):box('BatteryCellCap',(-.36,1.082,z),(.06,.018,.045),'Plastic','Node_Battery')
wire('UpperHose',[(.1,1,-1.31),(.17,1.02,-1.62),(.13,.91,-1.85)],.031,'Rubber')
rod('CoolantTank',(.43,.88,-.72),(.43,1.05,-.72),.075,'Coolant',n=12)
rod('TankCap',(.43,1.05,-.72),(.43,1.075,-.72),.045,'Plastic',n=12)
rod('BrakeBooster',(-.35,.89,-.55),(-.35,.89,-.69),.11,'Plastic','Node_Brakes',n=20)
box('BrakeReservoir',(-.35,1.025,-.65),(.13,.09,.15),'Coolant','Node_Brakes')
# Hood has a meaningful pivot at rear edge; vertices transformed to open position.
hinge=(0,1.015,-.47);start=len(parts)
box('Hood',(0,-.015,-.68),(1.18,.045,1.36),'Paint','Hood')
for x in (-.45,.45):box('HoodStiffener',(x,-.05,-.68),(.035,.025,1.18),'EngineMetal','Hood')
for z in (-.15,-1.15):box('HoodCrossBrace',(0,-.05,z),(1.0,.025,.035),'EngineMetal','Hood')
a=math.radians(73)
for part in parts[start:]:
 for v in part['vertices']:
  y,z=v['y'],v['z'];v['x']+=hinge[0];v['y']=round(hinge[1]+y*math.cos(a)-z*math.sin(a),6);v['z']=round(hinge[2]+y*math.sin(a)+z*math.cos(a),6)
rod('HoodProp',(.51,.95,-1.39),(.51,1.88,-.73),.007,'EngineMetal')
# Export independent formats. JSON uses Unity coordinates (Y up, front -Z).
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'SedanModel.json').write_text(json.dumps(dict(materials=[dict(name=n,color=list(c)) for n,c in MATS.items()],parts=parts),ensure_ascii=False,separators=(',',':')),encoding='utf-8')
lines=['# Diagnoz original procedural sedan study; metres, Y up, front -Z','mtllib SedanModel.mtl'];offset=1
for p in parts:
 lines += ['o '+p['name'],'g '+p['group'],'usemtl '+p['material']]
 for v in p['vertices']:lines.append('v {x:.6f} {y:.6f} {z:.6f}'.format(**v))
 for i in range(0,len(p['triangles']),3):lines.append('f '+' '.join(str(offset+k) for k in p['triangles'][i:i+3]))
 offset+=len(p['vertices'])
(OUT/'SedanModel.obj').write_text('\n'.join(lines)+'\n')
(OUT/'SedanModel.mtl').write_text('\n'.join('newmtl '+n+'\nKd '+' '.join(str(v) for v in c[:3])+'\nd '+str(c[3]) for n,c in MATS.items())+'\n')
print('Parts:',len(parts),'vertices:',sum(len(p['vertices']) for p in parts),'triangles:',sum(len(p['triangles'])//3 for p in parts))
