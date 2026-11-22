import json, copy, os
W='/home/user/workspace/CarDiagnosis_RC2_Work'
HINT=("Сравните показание с ориентиром учебного заказа. Это условная игровая модель: "
      "одно измерение не является универсальной диагностикой настоящего автомобиля.")
def obs(c,n,h,t,fh="",ft=""): return dict(checkId=c,nodeId=n,headline=h,text=t,fixedHeadline=fh,fixedText=ft)
BAT_OK="Напряжение аккумулятора в норме.\nОриентир учебного заказа: 12,6 В."
CLEAN="Клеммы чистые и затянуты, налёта нет."
base=dict(
 formatVersion=1,
 settings=dict(
  gameTitle="CAR DIAGNOSIS / МАСТЕРСКАЯ",
  subtitle="Учебные заказы · ресурс: время",
  introTitle="Смена в мастерской",
  introText=("Клиенты привозят машины с жалобами на запуск и электрику. Выберите узел, проведите проверки, "
             "сопоставьте наблюдения и выполните ремонт. После ремонта сделайте контрольную проверку этого узла. "
             "Время на заказ ограничено: каждое действие его тратит."),
  controlsText="ПКМ + мышь — вращение   /   Колесо — масштаб   /   ЛКМ — выбрать узел",
  disclaimer="Упрощённая игровая модель. Не руководство по ремонту автомобиля.",
  finalTitle="Смена закончена",
  finalText="Все заказы смены выполнены. Итоги по заказам:"),
 nodes=[dict(id="battery",name="Аккумулятор"),dict(id="alternator",name="Генератор"),
        dict(id="engine",name="Двигатель"),dict(id="brakes",name="Тормозной узел")],
 checks=[
  dict(id="multimeter",label="Мультиметр",cost=5,windowTitle="Мультиметр",hint=HINT,nodeIds=[],
       neutralHeadline="Без отклонений",neutralText="На узле «{node}» в этом заказе отклонений по напряжению нет."),
  dict(id="inspect_contacts",label="Осмотр контактов",cost=5,windowTitle="Осмотр контактов",hint="",nodeIds=[],
       neutralHeadline="Без отклонений",neutralText="Узел «{node}»: видимых повреждений и ослабленных креплений нет.")],
 repairs=[
  dict(id="replace_battery",label="Заменить аккумулятор",cost=20,resultName="Замена аккумулятора",nodeIds=["battery"]),
  dict(id="clean_terminals",label="Очистить клеммы",cost=15,resultName="Очистка и затяжка клемм",nodeIds=["battery"]),
  dict(id="replace_alternator",label="Заменить генератор",cost=25,resultName="Замена генератора",nodeIds=["alternator"]),
  dict(id="clean_ground",label="Зачистить массу",cost=15,resultName="Зачистка и затяжка массы двигателя",nodeIds=["engine"])],
 baseline=[
  obs("multimeter","battery","12,6 В",BAT_OK),
  obs("inspect_contacts","battery","Клеммы в порядке",CLEAN)],
 faults=[
  dict(id="weak_battery",fixedBy="replace_battery",observations=[
   obs("multimeter","battery","10,5 В","Напряжение аккумулятора ниже ориентира.\nОриентир учебного заказа: 12,6 В.","12,6 В",BAT_OK)]),
  dict(id="poor_contact",fixedBy="clean_terminals",observations=[
   obs("inspect_contacts","battery","Налёт на клеммах","Белый налёт на клеммах, минусовая клемма проворачивается рукой.",
       "Клеммы в порядке",CLEAN)]),
  dict(id="no_charging",fixedBy="replace_alternator",observations=[
   obs("multimeter","battery","12,2 В",
       "Замер при работающем двигателе: напряжение не выше, чем у стоящей машины. Заряд на аккумулятор не поступает.\nОриентир заряда: 13,8-14,5 В.",
       "14,0 В","Замер при работающем двигателе: заряд поступает.\nОриентир заряда: 13,8-14,5 В."),
   obs("multimeter","alternator","12,3 В",
       "Напряжение на выходе генератора при работающем двигателе ниже ориентира.\nОриентир заряда: 13,8-14,5 В.",
       "14,1 В","Напряжение на выходе генератора в пределах ориентира 13,8-14,5 В."),
   obs("inspect_contacts","alternator","Внешне в порядке",
       "Генератор закреплён, разъём на месте, видимых повреждений нет. Осмотр причину не показывает - нужен замер.")]),
  dict(id="bad_ground",fixedBy="clean_ground",observations=[
   obs("inspect_contacts","engine","Окисленная масса",
       "Провод массы на двигателе окислен, наконечник ослаблен. Через такой контакт стартеру не хватает тока.",
       "Масса в порядке","Провод массы зачищен и затянут, окислов нет."),
   obs("multimeter","engine","Показания скачут",
       "При прокрутке стартера показания на корпусе двигателя нестабильны. Похоже на плохой контакт в цепи массы.",
       "Стабильно","Показания на корпусе двигателя стабильны.")])],
 orders=[
  dict(id="order_01_no_start",title="Машина не заводится",
       complaint="При повороте ключа стартер еле крутит, двигатель не запускается. Посмотрите, пожалуйста, в чём дело",
       faultId="weak_battery",timeBudget=50),
  dict(id="order_02_intermittent",title="Заводится через раз",
       complaint="Иногда стартер только щёлкает, иногда крутит нормально. На кочках на приборной панели мигают лампы.",
       faultId="poor_contact",timeBudget=50),
  dict(id="order_03_charge_lamp",title="Горит лампа зарядки",
       complaint="В дороге загорелась лампа зарядки, утром машина завелась с трудом. Аккумулятор меняли месяц назад.",
       faultId="no_charging",timeBudget=50),
  dict(id="order_04_click_again",title="Снова щёлкает стартер",
       complaint="Стартер иногда только щёлкает, при запуске тускнеют фары. Клеммы аккумулятора недавно чистили.",
       faultId="bad_ground",timeBudget=45)])
rc1=copy.deepcopy(base)   # формат RC1 без type/goal/tireService — должен оставаться корректным
base["settings"]["introText"]=("Вы мастер небольшой мастерской. Подойдите к машине, примите заказ и выполните его: "
  "в диагностике осмотрите узлы, сопоставьте наблюдения и проведите ремонт с контрольной проверкой; "
  "в шиномонтаже снимите колесо, смените шину, отбалансируйте его и верните на место. "
  "Время на заказ ограничено: каждое действие его тратит.")
base["settings"]["subtitle"]="Учебные заказы · ресурс: время"
for o in base["orders"]:
    o["type"]="diagnosis"
    o["goal"]="Найти причину, устранить её и подтвердить результат контрольной проверкой."
base["orders"].append(dict(id="order_05_tire_season",type="tire_service",title="Сезонный шиномонтаж",
  complaint="Похолодало, хочу поставить зимнюю шину. Начните с переднего левого колеса, остальные сделаю позже.",
  goal="Снять переднее левое колесо, сменить шину на зимнюю, отбалансировать и установить колесо обратно.",
  faultId="",timeBudget=50))
base["tireService"]=dict(wheelName="Переднее левое колесо",fromTire="летняя",toTire="зимняя",
  imbalanceSide="наружная сторона диска",removeCost=5,dismountCost=8,mountCost=8,spinCost=3,weightCost=3,
  installCost=5,controlCost=3,imbalanceGrams=25,weightOptions=[15,25,35])
ext=copy.deepcopy(base)
ext["orders"].append(dict(id="order_06_long_parking",type="diagnosis",title="После долгой стоянки",
  complaint="Машина три недели стояла во дворе. Сегодня стартер почти не крутит, лампы на панели тусклые.",
  goal="Найти причину, устранить её и подтвердить результат контрольной проверкой.",
  faultId="weak_battery",timeBudget=35))
def dump(o,p):
    os.makedirs(os.path.dirname(p),exist_ok=True)
    open(p,'w',encoding='utf-8',newline='\n').write(json.dumps(o,ensure_ascii=False,indent=2)+"\n")
dump(base,W+'/Assets/StreamingAssets/CarDiagnosis/orders.json')
dump(base,W+'/Assets/Diagnoz/Resources/CarDiagnosis/DefaultCatalog.json')
E=W+'/CatalogExamples'
dump(base,E+'/orders_base.json'); dump(ext,E+'/orders_extended.json'); dump(rc1,E+'/orders_rc1_compat.json')
tf=copy.deepcopy(base); tf["orders"]=[tf["orders"][4]]+tf["orders"][:4]; dump(tf,E+'/orders_tire_first.json')
b=copy.deepcopy(base); b["orders"][1]["id"]=b["orders"][0]["id"]; dump(b,E+'/invalid/duplicate_order_id.json')
b=copy.deepcopy(base); b["orders"][2]["faultId"]="no_such_fault"; b["repairs"][0]["nodeIds"]=["trunk"]; dump(b,E+'/invalid/missing_reference.json')
b=copy.deepcopy(base); b["repairs"][1]["cost"]=0; b["orders"][3]["timeBudget"]=15; dump(b,E+'/invalid/bad_cost_and_time.json')
b=copy.deepcopy(base); b["orders"][4]["timeBudget"]=30; b["tireService"]["imbalanceGrams"]=20; b["orders"][0]["type"]="painting"; dump(b,E+'/invalid/bad_tire_service.json')
b=copy.deepcopy(base); del b["tireService"]; dump(b,E+'/invalid/missing_tire_service.json')
open(E+'/invalid/broken_json.json','w').write('{ "formatVersion": 1, "orders": [ { "id": "x", ')
open(E+'/invalid/empty_catalog.json','w').write('{}\n')
open(E+'/invalid/empty_file.json','w').write('')
print("ok")
