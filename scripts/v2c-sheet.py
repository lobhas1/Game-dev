import json, glob, secrets
old={json.load(open(f))['concept']['name']:json.load(open(f))['concept']['flavor'] for f in glob.glob('arena/fusions-v3-archive/*.record.json')}
new={json.load(open(f))['concept']['name']:json.load(open(f))['concept']['flavor'] for f in glob.glob('arena/fusions/*.record.json')}
sheet,key=[],[]
for i,n in enumerate(sorted(set(old)&set(new)),1):
    v4A=secrets.randbits(1)
    a,b=(new[n],old[n]) if v4A else (old[n],new[n])
    sheet.append(f"{i}. {n}\n   A: {a}\n   B: {b}")
    key.append(f"{i}. {n}: v4 = {'A' if v4A else 'B'}")
open('v2c-sheet.txt','w').write('\n'.join(sheet))
open('v2c-key.txt','w').write('\n'.join(key))
print("wrote v2c-sheet.txt (open) and v2c-key.txt (SEALED until picks recorded)")