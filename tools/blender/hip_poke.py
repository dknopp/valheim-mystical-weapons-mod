# Dev check (see docs/SHARDPLATE.md). Usage: python tools/blender/hip_poke.py <export dir>... (each holding
# ShardplateBreastplate/ and ShardplateGreaves/). Blender's bundled Python is enough, it needs numpy.
# Counts greaves vertices outside the breastplate's torso plates (Shardplate_1), per height slice and angle sector
import sys, numpy as np
def load(path, want=None):
    V, mat, keep = [], None, set()
    lines = open(path).read().split("\n")
    for l in lines:
        p = l.split()
        if not p: continue
        if p[0] == "v": V.append([-float(p[1]), float(p[2]), float(p[3])])
        elif p[0] == "usemtl": mat = p[1]
        elif p[0] == "f" and (want is None or mat == want):
            for t in p[1:]: keep.add(int(t.split("/")[0]) - 1)
    V = np.array(V); return V[sorted(keep)]
for d in sys.argv[1:]:
    T = load(f"{d}/ShardplateBreastplate/skin.obj", "Shardplate_1")
    G = load(f"{d}/ShardplateGreaves/skin.obj")
    lo = T[:, 1].min(); top = G[:, 1].max()
    out, n, worst = 0, 0, 0.0
    for y in np.arange(lo, top, 0.02):
        t = T[np.abs(T[:, 1] - y) < 0.015]; g = G[np.abs(G[:, 1] - y) < 0.01]
        if len(t) < 20 or not len(g): continue
        cz = (t[:, 2].max() + t[:, 2].min()) / 2
        at = np.arctan2(t[:, 2] - cz, t[:, 0]); rt = np.hypot(t[:, 2] - cz, t[:, 0])
        ag = np.arctan2(g[:, 2] - cz, g[:, 0]); rg = np.hypot(g[:, 2] - cz, g[:, 0])
        for k in range(len(g)):
            m = np.abs(np.angle(np.exp(1j * (at - ag[k])))) < 0.13
            if not m.any(): continue
            n += 1
            if rg[k] > rt[m].max(): out += 1; worst = max(worst, rg[k] - rt[m].max())
    print(f"{d.split('/')[-1]}: hip plates down to {lo:.3f} m, greaves top {top:.3f} m; {out} of {n} greaves vertices outside the hip plates, worst {worst*100:.1f} cm")
