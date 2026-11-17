"""
Nude body proxy and hands (the head is face.py). The body is mostly a skinning/fitting proxy (clothes cover it); the neck and the
hands are visible parts. All meshes are built in world coordinates in the A-pose of skeleton.py.
"""
from kit import *
import skeleton as sk
from mathutils import Euler

# per-character body shape (radii in metres); defaults describe an average adult
BODY_DEFAULT = dict(
    shoulder=1.0, chest=1.0, waist=1.0, hips=1.0, belly=0.0, arm=1.0, thigh=1.0, neck=1.0,
)


def body_params(**kw):
    p = dict(BODY_DEFAULT)
    p.update(kw)
    return p


# ----------------------------------------------------------------------------- smooth profiles
def catmull(tab, z):
    """Catmull-Rom interpolation of rows (z, a, b, c...) at z; returns a tuple of the interpolated columns."""
    n = len(tab)
    if z <= tab[0][0]:
        return tuple(tab[0][1:])
    if z >= tab[-1][0]:
        return tuple(tab[-1][1:])
    i = 0
    while tab[i + 1][0] < z:
        i += 1
    p0, p1, p2, p3 = tab[max(i - 1, 0)], tab[i], tab[i + 1], tab[min(i + 2, n - 1)]
    t = (z - p1[0]) / (p2[0] - p1[0])
    out = []
    for k in range(1, len(p1)):
        a, b, c, d = p0[k], p1[k], p2[k], p3[k]
        out.append(0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t ** 3))
    return tuple(out)


# torso cross-sections: z, half width, half depth, centre y (before the per-character scale)
TORSO = [
    (0.84, 0.140, 0.104, 0.006), (0.90, 0.166, 0.116, 0.006), (0.96, 0.162, 0.112, 0.005), (1.02, 0.146, 0.104, 0.003),
    (1.07, 0.138, 0.097, 0.002), (1.12, 0.144, 0.100, 0.001), (1.19, 0.158, 0.110, 0.000), (1.26, 0.166, 0.112, 0.000),
    (1.32, 0.162, 0.102, 0.002), (1.375, 0.136, 0.086, 0.004), (1.41, 0.100, 0.072, 0.004), (1.44, 0.060, 0.060, 0.003),
]


def torso_section(z, P, g=0.0):
    """Half width, half depth and centre y of the torso at height z for these body parameters, inflated by g."""
    rx, ry, cy = catmull(TORSO, z)
    wk = lerp(P['hips'], P['waist'], smoothstep(0.96, 1.04, z))
    wk = lerp(wk, P['chest'], smoothstep(1.10, 1.20, z))
    sw = lerp(1.0, P['shoulder'], smoothstep(1.26, 1.34, z))
    belly = P['belly'] * smoothstep(0.98, 1.04, z) * (1.0 - smoothstep(1.10, 1.16, z))
    return rx * wk * sw + g, ry * wk + g + belly, cy


def loft_rings(bm, sections, seg=48):
    """Closed lofted solid through elliptical rings: sections are (z, half width, half depth, centre y)."""
    rings = []
    for z, rx, ry, cy in sections:
        rings.append([bm.verts.new(V(rx * cos(2 * pi * i / seg), cy + ry * sin(2 * pi * i / seg), z)) for i in range(seg)])
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])


def torso_fill(bm, P, grow=0.0, top=1.45, bottom=0.84, shoulders=True, flare=None):
    """Pelvis, abdomen, ribcage and shoulder girdle: a loft of elliptical rings plus the shoulder bar and trapezius.
    'flare' = (length, extra) widens the bottom of the loft, as a jacket hem does."""
    g = grow
    n = max(2, int(round((top - bottom) / 0.015)))
    secs = []
    for i in range(n + 1):
        z = lerp(bottom, top, i / n)
        extra = flare[1] * (1.0 - smoothstep(bottom, bottom + flare[0], z)) if flare else 0.0
        rx, ry, cy = torso_section(z, P, g + extra)
        secs.append((z, rx, ry, cy))
    loft_rings(bm, secs, seg=56)
    if not shoulders:
        return
    sx = sk.SH_X * P['shoulder']
    bm_capsule(bm, (-sx + 0.02, 0.0, 1.385), (sx - 0.02, 0.0, 1.385), 0.050 + g, 0.050 + g, seg=16)
    for s in (-1, 1):       # trapezius: the slope from the neck to the shoulders
        rot = Euler((0, radians(s * 24), 0)).to_matrix()
        bm_ellipsoid(bm, (s * 0.075, 0.004, 1.418), (0.085, 0.060, 0.038 + g), rot, 20)


def arm_fill(bm, side, P, grow=0.0, hand=True, shoulder_r=0.058):
    m = (lambda v: v) if side == 'L' else sk.mirror
    sh, el, wr = m(sk.J['shoulder']), m(sk.J['elbow']), m(sk.J['wrist'])
    k = P['arm']
    bm_sphere(bm, sh, shoulder_r * k + grow, 16)
    bm_capsule(bm, sh, el, 0.052 * k + grow, 0.041 * k + grow, seg=16)
    bm_capsule(bm, el, wr, 0.040 * k + grow, 0.030 * k + grow, seg=16)


def leg_fill(bm, side, P, grow=0.0, foot=True, ankle_grow=None):
    """Thigh, knee, calf and (optionally) foot. 'ankle_grow' lets the tube end narrower than it started (trousers, boots)."""
    m = (lambda v: v) if side == 'L' else sk.mirror
    hip, kn, an, ball, toe = m(sk.J['hip']), m(sk.J['knee']), m(sk.J['ankle']), m(sk.J['ball']), m(sk.J['toe'])
    k = P['thigh']
    ga = grow if ankle_grow is None else ankle_grow
    bm_capsule(bm, hip + V(0, 0, 0.0), kn, 0.092 * k + grow, 0.062 * k + grow, seg=18)
    # calf bulge
    calf = kn.lerp(an, 0.30)
    bm_capsule(bm, kn, calf, 0.062 * k + grow, 0.060 * k + grow, seg=16)
    bm_capsule(bm, calf, an, 0.060 * k + grow, 0.040 * k + ga, seg=16)
    if foot:
        bm_ellipsoid(bm, an + V(0, -0.045, -0.040), (0.046, 0.115, 0.040), seg=20)
        bm_ellipsoid(bm, an + V(0, 0.030, -0.025), (0.042, 0.050, 0.050), seg=16)


NECK_TOP = V(0, -0.010, 1.525)


def neck_fill(bm, P, grow=0.0):
    k = P['neck']
    bm_capsule(bm, (0, 0.002, 1.40), NECK_TOP, 0.058 * k + grow, 0.051 * k + grow, seg=16)


def build_body(P, name='body', voxel=0.007):
    """Nude proxy: torso + arms (to the wrist) + legs + neck. Head and hands are separate objects."""
    def fill(bm):
        torso_fill(bm, P)
        for s in ('L', 'R'):
            arm_fill(bm, s, P)
            leg_fill(bm, s, P)
        neck_fill(bm, P)
    return organic(name, fill, voxel, sm_iter=5, sm_fac=0.45)


# ----------------------------------------------------------------------------- head
# The head, the ears and the eyes are sculpted in face.py (a signed-distance field); this module keeps the names the pipeline uses.
import face as _face
EYE_C, EYE_R = _face.EYE_C, _face.EYE_R
build_head, build_ears, build_eyes = _face.build_head, _face.build_ears, _face.build_eyes
head_profile = _face.midline


# ----------------------------------------------------------------------------- hands
# curl angles (degrees) per finger at the knuckle, middle and tip joints: a relaxed half-closed hand
HAND_CURL = {
    'Index': (28, 38, 22), 'Middle': (32, 42, 24), 'Ring': (36, 44, 26), 'Pinky': (40, 46, 26),
}
FINGER_SPEC = {
    # name: (offset along the thumb axis from the palm centre line, segment lengths, radii at knuckle/mid/dip/tip)
    'Index': (0.032, (0.045, 0.027, 0.022), (0.0105, 0.0098, 0.0088, 0.0076)),
    'Middle': (0.011, (0.050, 0.030, 0.023), (0.0108, 0.0100, 0.0090, 0.0078)),
    'Ring': (-0.011, (0.047, 0.028, 0.022), (0.0102, 0.0094, 0.0084, 0.0073)),
    'Pinky': (-0.031, (0.037, 0.022, 0.019), (0.0092, 0.0084, 0.0075, 0.0066)),
}


def hand_frame():
    """Left hand frame at the wrist: (origin, f along the fingers, p into the palm, a toward the thumb)."""
    W = sk.J['wrist']
    f = sk.ARM_HD_DIR.normalized()
    p = (V(-1, 0, 0) - f * f.dot(V(-1, 0, 0))).normalized()
    a = p.cross(f).normalized()
    return W, f, p, a


def compute_hand():
    """Finger joints of the left hand (fills skeleton.FINGERS) and the capsules that make the hand: (segments, frame)."""
    W, f, p, a = hand_frame()
    sk.FINGERS.clear()
    palm_len = 0.092

    def at(u, v, w):
        return W + a * u + f * v + p * w

    segs = []   # (a, b, ra, rb)
    for fn, (u0, lens, radii) in FINGER_SPEC.items():
        c1, c2, c3 = [radians(x) for x in HAND_CURL[fn]]
        P0 = at(u0, palm_len, 0.0)
        d1 = Quaternion(a, -c1) @ f
        d2 = Quaternion(a, -(c1 + c2)) @ f
        d3 = Quaternion(a, -(c1 + c2 + c3)) @ f
        P1 = P0 + d1 * lens[0]
        P2 = P1 + d2 * lens[1]
        P3 = P2 + d3 * lens[2]
        sk.FINGERS[fn] = [P0, P1, P2, P3]
        segs += [(P0, P1, radii[0], radii[1]), (P1, P2, radii[1], radii[2]), (P2, P3, radii[2], radii[3])]
    # thumb: leaves the palm toward the thumb side, partly toward the fingers and into the palm
    T0 = at(0.026, 0.034, 0.008)
    t_dir = (a * 0.30 + f * 0.85 + p * 0.28).normalized()
    t1 = T0 + t_dir * 0.036
    t_dir2 = (a * 0.10 + f * 0.78 + p * 0.55).normalized()
    t2 = t1 + t_dir2 * 0.030
    t_dir3 = (a * 0.00 + f * 0.55 + p * 0.78).normalized()
    t3 = t2 + t_dir3 * 0.026
    sk.FINGERS['Thumb'] = [T0, t1, t2, t3]
    segs += [(T0, t1, 0.0128, 0.0116), (t1, t2, 0.0116, 0.0102), (t2, t3, 0.0102, 0.0086)]
    return segs


def build_hand_left(P, grow=0.0, cuff=0.0, name='hand_L', voxel=0.0030):
    """Left hand. 'grow' inflates it (gloves), 'cuff' adds a gauntlet of that length over the wrist. Fills skeleton.FINGERS."""
    segs = compute_hand()
    W, f, p, a = hand_frame()

    def at(u, v, w):
        return W + a * u + f * v + p * w

    def fill(bm):
        g = grow
        bm_ellipsoid(bm, at(0.0, 0.045, -0.002), (0.044 + g, 0.053 + g, 0.019 + g), None, 20)      # palm
        bm_ellipsoid(bm, at(0.026, 0.028, 0.006), (0.020 + g, 0.030 + g, 0.018 + g), None, 14)      # thenar
        for (A, B, ra, rb) in segs:
            bm_capsule(bm, A, B, ra + g, rb + g, seg=10)
        bm_capsule(bm, W - f * 0.020, W + f * 0.030, 0.0300 + g, 0.0300 + g, seg=18)               # wrist
        if cuff > 0.0:
            bm_capsule(bm, W - f * cuff, W + f * 0.030, 0.0345 + g, 0.0375 + g, seg=18)            # gauntlet
    return organic(name, fill, voxel, sm_iter=4, sm_fac=0.45)


def build_hands(P, **kw):
    left = build_hand_left(P, **kw)
    right = left.copy()
    right.data = left.data.copy()
    right.name = 'hand_R'
    right.data.name = 'hand_R'
    link(right)
    mirror_x(right)
    return left, right
