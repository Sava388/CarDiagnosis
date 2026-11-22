# Синтаксический разбор tree-sitter (НЕ компиляция).
import glob, tree_sitter_c_sharp as tscs
from tree_sitter import Language, Parser
p=Parser(Language(tscs.language()))
bad=0
for f in sorted(glob.glob((__import__('os').environ.get('CD_ROOT','.'))+'/Assets/**/*.cs',recursive=True)):
    t=p.parse(open(f,'rb').read())
    errs=[]
    def walk(n):
        if n.type=='ERROR' or n.is_missing: errs.append(n.start_point)
        for c in n.children: walk(c)
    walk(t.root_node)
    print(('OK  ' if not errs else 'ERR ')+f.split('Assets/')[1], errs[:3]); bad+=bool(errs)
print('ошибок разбора:',bad)
