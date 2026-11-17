"""
Skeleton, rest pose and skin-weight rules shared by every character (same bones, same proportions, 1.75 m tall).

Rest pose: A-pose, feet flat, facing -Y, Z up, the character's left is +X. Bone names end in _L / _R. The weights are computed
from rules, not painted: chains with smooth hand-over zones at the joints, one region per limb, blended near the armpit and the
crotch. They work on the nude body and on any garment built around it, because they only look at the rest-pose position.
"""
from kit import *

SH_X = 0.185          # shoulder joint, half distance
HIP_X = 0.09
UPPER, LOWER, HAND_LEN = 0.30, 0.26, 0.095
ARM_UP_DIR = V(0.469, 0.0, -0.883)       # upper arm, 28 degrees out
ARM_LO_DIR = V(0.311, -0.124, -0.952)    # forearm, a little forward
ARM_HD_DIR = V(0.25, -0.10, -0.96)

# spine chain heights (the joints sit on the x = 0 plane)
Z_HIPS, Z_SPINE, Z_CHEST, Z_UCHEST, Z_NECK, Z_HEAD, Z_TOP = 0.93, 1.00, 1.14, 1.30, 1.44, 1.53, 1.77
HEAD_C = V(0.0, 0.0, 1.63)               # centre of the head (eye line)


def joints():
    """Left-side joints; the right side mirrors x."""
    sh = V(SH_X, 0.0, 1.40)
    el = sh + ARM_UP_DIR * UPPER
    wr = el + ARM_LO_DIR.normalized() * LOWER
    kn = wr + ARM_HD_DIR.normalized() * HAND_LEN
    return {
        'clav': V(0.03, -0.015, 1.395), 'shoulder': sh, 'elbow': el, 'wrist': wr, 'knuckle': kn,
        'hip': V(HIP_X, 0.0, 0.925), 'knee': V(0.095, -0.012, 0.505), 'ankle': V(0.095, 0.015, 0.09),
        'ball': V(0.095, -0.115, 0.035), 'toe': V(0.095, -0.205, 0.028),
    }


J = joints()


def mirror(v):
    return V(-v.x, v.y, v.z)


# finger joints are filled by body.build_hands(): FINGERS[name] = [p0 (knuckle), p1, p2, tip] for the LEFT hand
FINGERS = {}
FINGER_NAMES = ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']


def finger_bones():
    """(name, head, tail, parent) for the finger bones of one side, left positions."""
    out = []
    for fn in FINGER_NAMES:
        pts = FINGERS.get(fn)
        if not pts:
            continue
        par = 'Hand'
        for i in range(len(pts) - 1):
            nm = f'{fn}{i + 1}'
            out.append((nm, pts[i], pts[i + 1], par))
            par = nm
    return out


def bone_specs():
    """List of (name, head, tail, parent, deform) in creation order."""
    s = []
    s.append(('Hips', V(0, 0, Z_HIPS), V(0, 0, Z_SPINE), None))
    s.append(('Spine', V(0, 0, Z_SPINE), V(0, 0, Z_CHEST), 'Hips'))
    s.append(('Chest', V(0, 0, Z_CHEST), V(0, 0, Z_UCHEST), 'Spine'))
    s.append(('UpperChest', V(0, 0, Z_UCHEST), V(0, 0, Z_NECK), 'Chest'))
    s.append(('Neck', V(0, 0, Z_NECK), V(0, -0.012, Z_HEAD), 'UpperChest'))
    s.append(('Head', V(0, -0.012, Z_HEAD), V(0, -0.012, Z_TOP), 'Neck'))
    for side in ('L', 'R'):
        m = (lambda v: v) if side == 'L' else mirror
        s.append((f'Clavicle_{side}', m(J['clav']), m(J['shoulder']), 'UpperChest'))
        s.append((f'UpperArm_{side}', m(J['shoulder']), m(J['elbow']), f'Clavicle_{side}'))
        s.append((f'LowerArm_{side}', m(J['elbow']), m(J['wrist']), f'UpperArm_{side}'))
        s.append((f'Hand_{side}', m(J['wrist']), m(J['knuckle']), f'LowerArm_{side}'))
        for nm, h, t, par in finger_bones():
            s.append((f'{nm}_{side}', m(h), m(t), f'{par}_{side}'))
        s.append((f'Thigh_{side}', m(J['hip']), m(J['knee']), 'Hips'))
        s.append((f'Shin_{side}', m(J['knee']), m(J['ankle']), f'Thigh_{side}'))
        s.append((f'Foot_{side}', m(J['ankle']), m(J['ball']), f'Shin_{side}'))
        s.append((f'Toe_{side}', m(J['ball']), m(J['toe']), f'Foot_{side}'))
    return s


# ----------------------------------------------------------------------------- weight rules
def seg_t(p, a, b):
    """(t clamped 0..1, distance to the segment, unclamped t)."""
    ab = b - a
    L2 = ab.length_squared
    tu = (p - a).dot(ab) / L2 if L2 > 1e-12 else 0.0
    t = clamp(tu)
    return t, (p - (a + ab * t)).length, tu


def polyline_s(p, pts):
    """Arc length along the polyline at the projection of p, and the distance to it."""
    best = (1e9, 0.0)
    acc = 0.0
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        t, d, _ = seg_t(p, a, b)
        L = (b - a).length
        if d < best[0]:
            best = (d, acc + t * L)
        acc += L
    return best[1], best[0]


def chain(s, starts, widths, n):
    """Partition of unity along a chain of n bones: bone k is active from starts[k] (blend half width widths[k]) to starts[k+1]."""
    sig = [1.0]
    for k in range(1, n):
        sig.append(smoothstep(starts[k] - widths[k], starts[k] + widths[k], s))
    sig.append(0.0)
    return [sig[k] - sig[k + 1] for k in range(n)]


def add(w, name, v):
    if v > 1e-5:
        w[name] = w.get(name, 0.0) + v


SPINE_BONES = ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck', 'Head']
SPINE_STARTS = [Z_HIPS, Z_SPINE, Z_CHEST, Z_UCHEST, Z_NECK, Z_HEAD]
SPINE_WIDTHS = [0.0, 0.05, 0.06, 0.06, 0.03, 0.025]


def torso_weights(p, w=None, scale=1.0):
    w = {} if w is None else w
    parts = chain(p.z, SPINE_STARTS, SPINE_WIDTHS, len(SPINE_BONES))
    for nm, v in zip(SPINE_BONES, parts):
        add(w, nm, v * scale)
    return w


def leg_weights(p, side, w=None, scale=1.0, skirt=0.0):
    """Hips/thigh/shin/foot/toe for a vertex of the given side. 'skirt' keeps more of the Hips for loose lower garments."""
    w = {} if w is None else w
    m = (lambda v: v) if side == 'L' else mirror
    pts = [m(J['hip']), m(J['knee']), m(J['ankle']), m(J['ball']), m(J['toe'])]
    s, _ = polyline_s(p, pts)
    L0 = (pts[1] - pts[0]).length
    L1 = (pts[2] - pts[1]).length
    L2 = (pts[3] - pts[2]).length
    starts = [0.0, L0, L0 + L1, L0 + L1 + L2]
    widths = [0.0, 0.07, 0.045, 0.04]
    parts = chain(s, starts, widths, 4)
    names = [f'Thigh_{side}', f'Shin_{side}', f'Foot_{side}', f'Toe_{side}']
    # the foot sole region below the ankle is straightforward; the toe takes over past the ball of the foot
    hips_share = skirt * smoothstep(0.90, 0.35, p.z)
    for nm, v in zip(names, parts):
        add(w, nm, v * scale * (1.0 - (hips_share if nm.startswith('Thigh') else 0.0)))
    add(w, 'Hips', scale * hips_share * parts[0])
    return w


def arm_weights(p, side, w=None, scale=1.0, hand_split=None):
    """UpperChest (shoulder girdle), upper arm, lower arm and hand for a vertex of the given side."""
    w = {} if w is None else w
    m = (lambda v: v) if side == 'L' else mirror
    sh, el, wr, kn = m(J['shoulder']), m(J['elbow']), m(J['wrist']), m(J['knuckle'])
    s, _ = polyline_s(p, [sh, el, wr, kn])
    L0 = (el - sh).length
    L1 = (wr - el).length
    starts = [-0.12, 0.0, L0, L0 + L1]
    widths = [0.0, 0.07, 0.05, 0.03]
    parts = chain(s, starts, widths, 4)
    add(w, 'UpperChest', parts[0] * scale)
    add(w, f'UpperArm_{side}', parts[1] * scale)
    add(w, f'LowerArm_{side}', parts[2] * scale)
    hand = parts[3] * scale
    if hand_split is None:
        add(w, f'Hand_{side}', hand)
    else:
        for nm, v in hand_split.items():
            add(w, nm, v * hand)
    return w


def armness(p, side, margin=0.0):
    """How much a vertex belongs to the arm of this side (1 inside the arm tube, 0 on the torso)."""
    m = (lambda v: v) if side == 'L' else mirror
    sh, el, wr, kn = m(J['shoulder']), m(J['elbow']), m(J['wrist']), m(J['knuckle'])
    pts = [sh, el, wr, kn]
    s, d = polyline_s(p, pts)
    L = (el - sh).length + (wr - el).length
    r = lerp(0.060, 0.040, clamp(s / L)) + margin
    # in front of the shoulder joint, along the axis, the point is torso unless it is inside the tube
    return 1.0 - smoothstep(r * 1.15, r * 1.85, d)


def legness(p, margin=0.0):
    """How much a vertex belongs to a leg rather than the pelvis (1 below the crotch, 0 above the hip joint)."""
    return smoothstep(0.945 + margin, 0.835 + margin, p.z)


def finger_split(p, side, tol=1.9):
    """Weights among the hand and finger bones for a glove vertex (left coordinates mirrored for the right side)."""
    q = p if side == 'L' else mirror(p)
    best = None
    for fn, pts in FINGERS.items():
        for i in range(len(pts) - 1):
            t, d, _ = seg_t(q, pts[i], pts[i + 1])
            if best is None or d < best[0]:
                best = (d, fn, i, t)
    out = {}
    if best is None:
        return {f'Hand_{side}': 1.0}
    d, fn, i, t = best
    r = 0.011
    if d > r * tol:
        return {f'Hand_{side}': 1.0}
    k = 1.0 - smoothstep(r * 1.1, r * tol, d)        # 1 near a finger bone, 0 at the palm
    nm = f'{fn}{i + 1}_{side}'
    prev = f'{fn}{i}_{side}' if i > 0 else f'Hand_{side}'
    # blend with the previous bone near the joint
    j = smoothstep(0.0, 0.45, t)
    out[nm] = k * j
    out[prev] = out.get(prev, 0.0) + k * (1.0 - j)
    out[f'Hand_{side}'] = out.get(f'Hand_{side}', 0.0) + (1.0 - k)
    return out


def body_weights(p, skirt=0.0, arm_margin=0.0, fingers=True, leg_scale=1.0):
    """Final weight dictionary for a vertex at rest-pose position p. leg_scale = 0 keeps a jacket hem on the hips and spine."""
    side = 'L' if p.x >= 0 else 'R'
    a = armness(p, side, arm_margin)
    L = legness(p) * leg_scale if p.z < 1.0 else 0.0
    w = {}
    # torso/pelvis part
    tw = torso_weights(p)
    # legs
    lw = leg_weights(p, side, skirt=skirt) if L > 0 else {}
    # blend legs with torso, then arms
    base = {}
    for k, v in tw.items():
        add(base, k, v * (1.0 - L))
    for k, v in lw.items():
        add(base, k, v * L)
    if a > 1e-4:
        hs = finger_split(p, side) if fingers else None
        aw = arm_weights(p, side, hand_split=hs)
        for k, v in aw.items():
            add(w, k, v * a)
        for k, v in base.items():
            add(w, k, v * (1.0 - a))
    else:
        w = base
    # a little clavicle on the shoulder top
    return w


def hand_weights(p, fingers=True):
    """Weights of a glove or cuff vertex: the arm chain only (hands hang next to the hips, so never mix in the legs)."""
    side = 'L' if p.x >= 0 else 'R'
    return arm_weights(p, side, hand_split=finger_split(p, side) if fingers else None)


def rigid_weights(bone):
    return {bone: 1.0}
