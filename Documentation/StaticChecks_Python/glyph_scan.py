# RC2. Поиск символов вне набора шрифта в строковых литералах C# и в JSON каталогов.
import glob,re,os,json
W=os.environ.get('CD_ROOT','/home/user/workspace/CarDiagnosis_RC2_Work')
OK=set(chr(i) for i in range(32,127))|set(chr(i) for i in range(0x410,0x450))|set("Ёё—·№«»")
bad=0
for f in sorted(x for x in glob.glob(W+'/Assets/**/*.cs',recursive=True) if '/Tests/' not in x):  # в тестах намеренный символ вне шрифта
    for n,line in enumerate(open(f,encoding='utf-8'),1):
        code=line.split('//')[0] if '"' not in line.split('//')[0][-1:] else line
        for lit in re.findall(r'"((?:[^"\\]|\\.)*)"',code):
            b=set(lit)-OK
            if b: print(os.path.relpath(f,W),n,sorted(b),lit[:60]); bad+=1
for f in glob.glob(W+'/CatalogExamples/**/*.json',recursive=True)+[W+'/Assets/StreamingAssets/CarDiagnosis/orders.json',W+'/Assets/Diagnoz/Resources/CarDiagnosis/DefaultCatalog.json']:
    t=open(f,encoding='utf-8').read(); b=set(t)-OK-set('\n\r\t')
    if b: print(os.path.relpath(f,W),sorted(b)); bad+=1
print('найдено проблем:',bad)
