# Finds places for the mod's tech nodes in the game's tech tree: no card may cover another card or a connecting
# line, and the new connecting lines may not run through cards or along other lines. Input: science.json, a dump
# of the game's Science table (ID, ScienceGroup, HexGridX, HexGridY, PathNode). Output: layout.json (pre, x, y, path).
# Card size is measured from screenshots of the game's tree: icon at the anchor, card ~310 units to the right, +-50 up/down.
import json
rows=[r for r in json.load(open('science.json')) if r['ScienceGroup']=='Main']
pos={r['ID']:(r['HexGridX'],r['HexGridY']) for r in rows}
CARD=(-70,310,-50,50)
cards=[]; segs=[]
def path_points(child, pathnode):
    out=[]
    for part in pathnode.split('|'):
        m=part.split(',',1)
        if len(m)<2 or m[0] not in pos: continue
        par=m[0]; rest=m[1]; kind=rest[0]; mid=rest[2:].rsplit(':',1)[0] if ':' in rest else ''
        px,py=pos[par]; pts=[(px+CARD[1],py) if kind=='r' else (px,py)]
        for w in filter(None, mid.split('_')):
            x,y=w.split(','); pts.append((float(x),float(y)))
        cx,cy=pos[child]; pts.append((cx+CARD[0],cy)); out.append(pts)
    return out
for r in rows:
    x,y=pos[r['ID']]; cards.append((x+CARD[0],x+CARD[1],y+CARD[2],y+CARD[3],r['ID']))
    for pts in path_points(r['ID'], r['PathNode'] or ''):
        for a,b in zip(pts,pts[1:]): segs.append((a,b,r['ID']))
def rr(a,b,m=12): return not (a[1]+m<=b[0] or b[1]+m<=a[0] or a[3]+m<=b[2] or b[3]+m<=a[2])
def sr(s,r,m=10):
    (x1,y1),(x2,y2)=s[0],s[1]; X0,X1,Y0,Y1=r[0]-m,r[1]+m,r[2]-m,r[3]+m
    if x1==x2: return X0<=x1<=X1 and not (max(y1,y2)<Y0 or min(y1,y2)>Y1)
    if y1==y2: return Y0<=y1<=Y1 and not (max(x1,x2)<X0 or min(x1,x2)>X1)
    for k in range(41):   # diagonal: sample it
        t=k/40; x=x1+(x2-x1)*t; y=y1+(y2-y1)*t
        if X0<=x<=X1 and Y0<=y<=Y1: return True
    return False
def ss(a,b):   # two axis-aligned segments cross or overlap (perpendicular crossing only)
    (ax1,ay1),(ax2,ay2)=a; (bx1,by1),(bx2,by2)=b
    # perpendicular crossings are allowed (the game's own tree has them); collinear overlaps are not
    if abs(ax1-bx1)<12 and ax1==ax2 and bx1==bx2: return not (max(ay1,ay2)<min(by1,by2) or max(by1,by2)<min(ay1,ay2))
    if abs(ay1-by1)<12 and ay1==ay2 and by1==by2: return not (max(ax1,ax2)<min(bx1,bx2) or max(bx1,bx2)<min(ax1,ax2))
    return False
def free(x,y,parent,trunk=False):
    r=(x+CARD[0],x+CARD[1],y+CARD[2],y+CARD[3])
    if any(rr(r,c) for c in cards): return None
    if any(sr(s,r) for s in segs): return None
    px,py=pos[parent]
    if trunk:   # 't' path: straight up/down from the parent, then right to the card (as def-1 hangs off part-1)
        xm=px; path=[((px,py),(px,y)),((px,y),(x+CARD[0],y))]
    else:
        xm=x+CARD[0]-30; sx=px+CARD[1]
        if xm<=sx+10: return None
        path=[((sx,py),(xm,py)),((xm,py),(xm,y)),((xm,y),(x+CARD[0],y))]
    for p in path:
        if p[0]==p[1]: continue
        for c in cards:
            if c[4]==parent: continue
            if sr(p,c,4): return None
        for s in segs:
            if s[0] in ((px,py),(px+CARD[1],py)): continue      # the parent's own outgoing trunk may be shared
            if ss(p,(s[0],s[1])): return None
    return path, xm
want=[('ef-def-1','part-1'),('ef-def-2','def-2'),('ef-surge-2','226'),('ef-def-3','def-3'),('ef-chem-3','ef-def-3'),('ef-ray-3','chip-3'),('ef-heavy-4','2159'),('ef-ray-4','335')]
res={}
for nid,par in want:
    for k in [c for c in cards if c[4]==nid]: cards.remove(k)
    segs[:]=[s for s in segs if s[2]!=nid]
    px,py=pos[par]; best=None
    for dx,trunk in ((100,True),(400,False),(450,False),(500,False),(550,False),(800,False),(850,False),(900,False),(1200,False),(1250,False),(1600,False),(2000,False)):
        for y in sorted(range(-900,1700,25), key=lambda v: abs(v-py)):
            if trunk and abs(y-py)<100: continue
            f=free(px+dx,y,par,trunk)
            if f: best=(px+dx,y,f,trunk); break
        if best: break
    if not best: print('NO SPOT', nid); continue
    x,y,(path,xm),trunk=best; pos[nid]=(x,y)
    res[nid]=dict(pre=par,x=x,y=y,path=f'{par},t:{px},{y}:l' if trunk else (f'{par},r:{xm},{py}_{xm},{y}:l' if y!=py else f'{par},r::l'))
    cards.append((x+CARD[0],x+CARD[1],y+CARD[2],y+CARD[3],nid))
    for s in path: segs.append((s[0],s[1],nid))
    print(nid, 'after', par, (x,y), res[nid]['path'])
json.dump(res,open('layout.json','w'),indent=1)
