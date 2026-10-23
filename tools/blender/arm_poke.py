# Dev check (see docs/SHARDPLATE.md). Usage (Blender's bundled Python is enough, it needs numpy):
#   python tools/blender/arm_poke.py tools/blender/player_skeleton.json models/ShardplateBreastplate/skin.obj
# For each body sample point on the arms, measures it in the frame of its nearest arm segment
# (distance along the bone, angle around it) and compares its distance from the bone with the
# armour's in that direction. Prints the points that reach past the plates, worst first.
import sys, json
import numpy as np

skel = json.load(open(sys.argv[1]))
B = {b["name"]: np.array(b["position"]) for b in skel["bones"]}
body = np.array(skel["body"])
V = np.array([[-float(p[1]), float(p[2]), float(p[3])] for p in (l.split() for l in open(sys.argv[2])) if p and p[0] == "v"])
MARGIN = float(sys.argv[3]) if len(sys.argv) > 3 else 0.01  # plates are thin: body must sit this far inside


def chain(s):
    tip = (B[s + "HandMiddle3"] - B[s + "HandMiddle2"]) + B[s + "HandMiddle3"]
    return [B[s + "Arm"], B[s + "ForeArm"], B[s + "Hand"], tip]


def frame(P, a, b):
    ab = b - a; L = np.linalg.norm(ab); u = ab / L
    t = (P - a) @ u
    perp = P - a - t[:, None] * u
    # reference directions around the bone: "back" is world -Z projected off the bone
    back = np.array([0, 0, -1.0]) - u * (-u[2]); back /= np.linalg.norm(back)
    side = np.cross(u, back)
    return t / L, np.linalg.norm(perp, axis=1), np.arctan2(perp @ side, perp @ back)


bad_all = 0; n_all = 0
for s in ("Left", "Right"):
    C = chain(s)
    sgn = -1 if s == "Left" else 1
    pts = body[(np.sign(body[:, 0]) == sgn) & (np.abs(body[:, 0]) > 0.25) & (body[:, 1] > 0.85)]
    arm = V[(np.sign(V[:, 0]) == sgn) & (np.abs(V[:, 0]) > 0.2)]
    for p in pts:
        # nearest segment
        best = None
        for i, name in enumerate(("upper", "fore", "hand")):
            t, r, ang = frame(p[None], C[i], C[i + 1])
            tc = np.clip(t[0], 0, 1); d = np.linalg.norm(p - (C[i] + tc * (C[i + 1] - C[i])))
            if best is None or d < best[0]: best = (d, i, name, t[0], r[0], ang[0])
        _, i, name, t, r, ang = best
        if name == "upper" and t < 0.15: continue  # shoulder: torso/pauldron territory
        ta, ra, aa = frame(arm, C[i], C[i + 1])
        L = np.linalg.norm(C[i + 1] - C[i])
        m = (np.abs((ta - t) * L) < 0.03) & (np.abs(np.angle(np.exp(1j * (aa - ang)))) < 0.4)
        n_all += 1
        if m.sum() == 0:
            print(f"{s:5} {name:5} t={t:5.2f} dir={np.degrees(ang):5.0f}  r={r:.3f}  NO ARMOUR IN THAT DIRECTION   p={p.round(3)}")
            bad_all += 1; continue
        rmax = ra[m].max()
        if r > rmax - MARGIN:
            bad_all += 1
            print(f"{s:5} {name:5} t={t:5.2f} dir={np.degrees(ang):5.0f}  r={r:.3f}  armour {rmax:.3f}  out by {r - rmax + MARGIN:.3f}   p={p.round(3)}")
print(f"{bad_all} of {n_all} arm points poke out (dir 0 = back, +-90 = sides, 180 = front)")
