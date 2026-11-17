"""
The head of a survivor: a sculpted face (a signed-distance field of anatomical blobs and grooves, marched from inside the skull
onto an icosphere), the openings of the eyes with lid margins, the ears and the eyeballs. Everything is in head-local coordinates
(eye line z = 0, the face looks along -y, x > 0 is the character's left) and is moved to the skeleton's head centre at the end.

Why a field and not a mesh sculpt: every feature (nose, lips, brow, cheekbones, grooves) is one primitive with a few numbers in a
landmark table, the four characters differ in a dozen scalars (`face` of characters.SPEC), and the same field tells the skin material
where the lips, the nostrils or the beard zone are (`paint`), so that the painted features always sit on the sculpted ones.
"""
from kit import *
import numpy as np
from mathutils import Euler
import skeleton as sk

EYE_C = V(0.0305, -0.0690, 0.001)      # left eyeball centre in head-local space (the right one mirrors x)
EYE_R = 0.0127

# the centre the rays start from: inside the skull, behind the nose, between the eyes and the mouth
RAY_O = (0.0, -0.014, -0.024)
JAW_BACK = 0.014                          # the lower face stops this far behind the eye line (head-local y): see face_field


# ----------------------------------------------------------------------------- signed distance helpers (numpy, float32)
def f32(a):
    return np.asarray(a, dtype=np.float32)


def sd_ell(x, y, z, c, r, m=None):
    """Approximate signed distance to an ellipsoid (centre c, radii r); m is an optional 3x3 matrix taking world to local axes."""
    px, py, pz = x - c[0], y - c[1], z - c[2]
    if m is not None:
        px, py, pz = (m[0][0] * px + m[0][1] * py + m[0][2] * pz,
                      m[1][0] * px + m[1][1] * py + m[1][2] * pz,
                      m[2][0] * px + m[2][1] * py + m[2][2] * pz)
    ax, ay, az = px / r[0], py / r[1], pz / r[2]
    k0 = np.sqrt(ax * ax + ay * ay + az * az)
    k1 = np.sqrt((ax / r[0]) ** 2 + (ay / r[1]) ** 2 + (az / r[2]) ** 2)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-6)


def sd_sell(x, y, z, c, r, n=2.6):
    """Approximate signed distance to a superellipsoid: horizontal sections are superellipses of exponent n (flatter fronts and sides
    than an ellipse), vertical sections are ellipses."""
    ax = np.abs((x - c[0]) / r[0])
    ay = np.abs((y - c[1]) / r[1])
    az = (z - c[2]) / r[2]
    u = np.power(np.power(ax, n) + np.power(ay, n), 1.0 / n)
    return (np.sqrt(u * u + az * az) - 1.0) * min(r)


def sd_cap(x, y, z, a, b, ra, rb=None):
    """Signed distance to a capsule from a to b with radius ra at a and rb at b."""
    rb = ra if rb is None else rb
    ax, ay, az = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    l2 = ax * ax + ay * ay + az * az
    px, py, pz = x - a[0], y - a[1], z - a[2]
    t = np.clip((px * ax + py * ay + pz * az) / l2, 0.0, 1.0)
    dx, dy, dz = px - t * ax, py - t * ay, pz - t * az
    return np.sqrt(dx * dx + dy * dy + dz * dz) - (ra + (rb - ra) * t)


def sd_chain(x, y, z, pts, radii):
    """Signed distance to a chain of capsules through pts (a groove, a ridge, a brow)."""
    d = None
    for i in range(len(pts) - 1):
        di = sd_cap(x, y, z, pts[i], pts[i + 1], radii[i], radii[i + 1])
        d = di if d is None else np.minimum(d, di)
    return d


def dist2d(x, z, pts):
    """Distance in the (x, z) plane to a polyline of (x, z) points: creases are defined on the front projection of the face."""
    best = None
    for i in range(len(pts) - 1):
        ax, az = pts[i]
        bx, bz = pts[i + 1]
        vx, vz = bx - ax, bz - az
        l2 = vx * vx + vz * vz
        t = np.clip(((x - ax) * vx + (z - az) * vz) / l2, 0.0, 1.0)
        d = np.sqrt((x - ax - t * vx) ** 2 + (z - az - t * vz) ** 2)
        best = d if best is None else np.minimum(best, d)
    return best


def smin(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smax(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.maximum(a, b) + h * h * k * 0.25


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def rotm(rx=0.0, ry=0.0, rz=0.0):
    """World-to-local matrix of an ellipsoid turned by (rx, ry, rz) degrees (Euler XYZ)."""
    M = Euler((radians(rx), radians(ry), radians(rz))).to_matrix()
    return [[M[j][i] for j in range(3)] for i in range(3)]       # transpose = inverse


# ----------------------------------------------------------------------------- the face
DEFAULTS = dict(
    width=1.0, jaw=1.0, chin=1.0, brow=1.0, cheek=1.0, hollow=0.0,
    nose_h=1.0, nose_w=1.0, nose_len=1.0, bridge=1.0, nostril=1.0,
    lip=1.0, mouth_w=1.0, age=0.0,
    eye=1.0, eye_tilt=0.0, eye_gap=1.0,
)


def params(F):
    p = dict(DEFAULTS)
    p.update(F or {})
    return p


def lip_chain(p):
    """The mouth line (a gentle curve, the corners a little behind and higher): centre of the groove, in head-local space."""
    mw = p['mouth_w']
    return [(-0.0225 * mw, -0.0825, -0.0762), (-0.0150 * mw, -0.0870, -0.0782), (-0.0070 * mw, -0.0905, -0.0780),
            (0.0, -0.0915, -0.0776), (0.0070 * mw, -0.0905, -0.0780), (0.0150 * mw, -0.0870, -0.0782), (0.0225 * mw, -0.0825, -0.0762)]


def face_field(p):
    """Returns sdf(x, y, z) of the solid head and a dict of named sub-fields used for painting."""
    w, jw, ch, br, ck = p['width'], p['jaw'], p['chin'], p['brow'], p['cheek']
    nh, nw, nl, bridge = p['nose_h'], p['nose_w'], p['nose_len'], p['bridge']
    lip, mw, age, hollow = p['lip'], p['mouth_w'], p['age'], p['hollow']
    ex = EYE_C.x * p['eye_gap']
    ez = EYE_C.z
    sub = {}

    # ---- parts that are evaluated for painting as well as for the shape
    def upper_lip(X, y, z):
        return sd_ell(X, y, z, (0.0, -0.0818, -0.0712), (0.0214 * mw, 0.0088, 0.0060 * lip))

    def lower_lip(X, y, z):
        return sd_ell(X, y, z, (0.0, -0.0830, -0.0858), (0.0206 * mw, 0.0092, 0.0074 * lip))

    def nostril(X, y, z):
        return sd_ell(X, y, z, (0.0078 * nw, -0.0958 - 0.002 * nh, -0.0582), (0.0042 * p['nostril'], 0.0066, 0.0050), rotm(-14, 0, -12))

    def brow_chain(X, y, z):
        pts, rad = brow_curve(p)
        return sd_chain(X, y, z, pts, rad)

    sub.update(upper_lip=upper_lip, lower_lip=lower_lip, nostril=nostril, brow=brow_chain)

    stage = int(p.get('stage', 9))

    def sdf(x, y, z):
        X = np.abs(x)
        # A: skull, face plate, lower face
        d = sd_ell(X, y, z, (0.0, 0.012, 0.030), (0.0765 * w, 0.095, 0.090))
        d = smin(d, sd_sell(X, y, z, (0.0, -0.036, -0.022), (0.0675 * w, 0.050, 0.080), 3.0), 0.014)
        # the lower face: its back half would hang behind the neck as a ball, so it stops behind the jaw angle (the nape is the neck's)
        d = smin(d, smax(sd_sell(X, y, z, (0.0, -0.014, -0.080), (0.0465 * w * jw, 0.077, 0.046), 2.2), y - JAW_BACK, 0.020), 0.014)
        if stage < 1:
            return d
        # B: brow ridge, cheekbones, jaw angle, chin
        d = smin(d, sd_chain(X, y, z, [(0.0, -0.0790, 0.0240), (0.026, -0.0770, 0.0250), (0.050 * w, -0.0595, 0.0225)],
                             [0.0058 * br, 0.0058 * br, 0.0048 * br]), 0.008)
        d = smin(d, sd_ell(X, y, z, (0.0415 * w * jw, 0.008, -0.064), (0.0085, 0.020, 0.016)), 0.014)
        d = smin(d, sd_ell(X, y, z, (0.0, -0.0782, -0.1070), (0.0140 * ch, 0.0140, 0.0132 * ch)), 0.010)
        d = smin(d, sd_ell(X, y, z, (0.0425 * w, -0.0600, -0.021), (0.0200, 0.0120, 0.0090 * ck), rotm(0, -14, 0)), 0.012)
        d = smin(d, sd_ell(X, y, z, (0.0600 * w, -0.0350, -0.013), (0.0095, 0.0260, 0.0070 * ck)), 0.010)
        if hollow:                  # gaunt cheeks under the cheekbones (a dent of the surface, never a void inside the head)
            d = d + 0.0040 * hollow * np.exp(-0.5 * (((X - 0.0480 * w) / 0.016) ** 2 + ((y + 0.045) / 0.024) ** 2 + ((z + 0.055) / 0.020) ** 2))
        if stage < 2:
            return d
        # C: nose: dorsum, tip, wings, base
        tipy = -0.0925 - 0.0048 * nh * nl
        tipz = -0.0455 - 0.0020 * nl
        d = smin(d, sd_chain(X, y, z, [(0.0, -0.0715, 0.010), (0.0, -0.0830, -0.020), (0.0, tipy + 0.004, tipz + 0.006)],
                             [0.0070 * bridge, 0.0082 * nw, 0.0094 * nw]), 0.006)
        d = smin(d, sd_ell(X, y, z, (0.0, tipy, tipz), (0.0098 * nw, 0.0095, 0.0090)), 0.005)
        d = smin(d, sd_ell(X, y, z, (0.0104 * nw, -0.0850 - 0.001 * nh, -0.0500), (0.0080, 0.0085, 0.0085), rotm(0, 0, 18)), 0.005)
        if stage < 3:
            return d
        # D: lips, chin crease, mouth line
        d = smin(d, upper_lip(X, y, z), 0.004)
        d = smin(d, lower_lip(X, y, z), 0.004)
        if stage < 4:
            return d
        # E: grooves: mouth line, chin crease, philtrum, nasolabial folds, nostrils
        mline = sd_chain(X, y, z, lip_chain(p), [0.0016] * 7)
        d = smax(d, -mline, 0.0014)
        d = smax(d, -sd_chain(X, y, z, [(-0.013, -0.0985, -0.0985), (0.0, -0.1000, -0.0990), (0.013, -0.0985, -0.0985)], [0.0028] * 3), 0.003)
        d = smax(d, -sd_chain(X, y, z, [(0.0058, -0.0965, -0.0615), (0.0058, -0.0985, -0.0690)], [0.0028, 0.0034]), 0.003)
        front = sstep(-0.045, -0.062, y)
        gf = np.exp(-0.5 * (dist2d(X, z, [(0.0205 * nw, -0.0535), (0.0262, -0.0640), (0.0298 * mw, -0.0775)]) / 0.0034) ** 2)
        d = d + 0.0016 * (0.7 + 1.5 * age) * gf * front                 # nasolabial folds
        d = smax(d, -nostril(X, y, z), 0.0025)
        if stage < 5:
            return d
        # F: the socket round the eye (the lid sphere comes back), eyelid crease, under-eye, temple hollow
        d = smax(d, -sd_ell(X, y, z, (ex, -0.0975, ez + 0.001), (0.0200, 0.0160, 0.0170)), 0.006)
        d = smin(d, sd_ell(X, y, z, (ex, EYE_C.y, ez), (EYE_R + 0.0012,) * 3), 0.0035)
        front = sstep(-0.045, -0.062, y)
        g = np.exp(-0.5 * (dist2d(X, z, [(ex - 0.0115, ez + 0.0125), (ex, ez + 0.0150), (ex + 0.0120, ez + 0.0132)]) / 0.0016) ** 2)
        d = d + 0.0009 * g * front                                    # the crease above the upper lid
        g = np.exp(-0.5 * (dist2d(X, z, [(ex - 0.010, ez - 0.0185), (ex + 0.002, ez - 0.0195), (ex + 0.014, ez - 0.0165)]) / 0.0020) ** 2)
        d = d + (0.0006 + 0.0010 * age) * g * front                   # the line under the lower lid
        d = d + 0.0016 * np.exp(-0.5 * (((X - 0.0700 * w) / 0.012) ** 2 + ((y + 0.034) / 0.026) ** 2 + ((z - 0.030) / 0.020) ** 2))   # temple
        return d

    return sdf, sub


def brow_curve(p):
    """The brow as a chain: (points, radii) in head-local space (left side)."""
    ex = EYE_C.x * p['eye_gap']
    t = p['brow']
    pts = [(ex - 0.0200, -0.0895, 0.0205), (ex - 0.0100, -0.0905, 0.0262), (ex + 0.0050, -0.0880, 0.0318),
           (ex + 0.0180, -0.0805, 0.0322), (ex + 0.0270, -0.0715, 0.0278)]
    rad = [0.0050 * t, 0.0050 * t, 0.0047 * t, 0.0036 * t, 0.0020 * t]
    return pts, rad


# ----------------------------------------------------------------------------- marching
def march(sdf, D, o=RAY_O, t0=0.020, t1=0.170, steps=60, refine=14):
    """First crossing of sdf = 0 along the rays o + t D from inside the solid (the surface seen from the inside): returns t (N,)."""
    n = D.shape[0]
    per_ray = isinstance(o, np.ndarray)
    ox, oy, oz = (o[:, 0], o[:, 1], o[:, 2]) if per_ray else o
    dx, dy, dz = D[:, 0], D[:, 1], D[:, 2]
    lo = np.full(n, t0, dtype=np.float32)
    hi = np.full(n, t1, dtype=np.float32)
    found = np.zeros(n, dtype=bool)
    prev = np.full(n, t0, dtype=np.float32)
    live = np.arange(n)
    for k in range(1, steps + 1):
        tk = t0 + (t1 - t0) * k / steps
        f = sdf((ox[live] if per_ray else ox) + tk * dx[live], (oy[live] if per_ray else oy) + tk * dy[live],
                (oz[live] if per_ray else oz) + tk * dz[live])
        hit = f >= 0.0
        idx = live[hit]
        lo[idx] = prev[idx]
        hi[idx] = tk
        found[idx] = True
        prev[live[~hit]] = tk
        live = live[~hit]
        if live.size == 0:
            break
    for _ in range(refine):
        mid = 0.5 * (lo + hi)
        f = sdf(ox + mid * dx, oy + mid * dy, oz + mid * dz)
        inside = f < 0.0
        lo = np.where(inside, mid, lo)
        hi = np.where(inside, hi, mid)
    return 0.5 * (lo + hi)


def sphere_dirs(subdiv):
    """Unit vectors of an icosphere and the bmesh that holds its topology."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.ensure_lookup_table()
    D = f32([[v.co.x, v.co.y, v.co.z] for v in bm.verts])
    D /= np.linalg.norm(D, axis=1, keepdims=True)
    return bm, D


# ----------------------------------------------------------------------------- the eye openings
PHI_OUT, PHI_IN = radians(72.0), radians(40.0)
EXPO_OUT, EXPO_IN = 1.7, 2.4         # the outer corner of the lid margin is a point, the inner one (towards the nose) is blunt
EPS_UP, EPS_LO = radians(23.0), radians(28.0)
TILT = 0.15


def window_rho(px, py, pz, s, ex, eye=1.0, tilt=0.0):
    """rho (lens-shaped distance to the lid margin), phi, eps, upper for points relative to the eye of side s centred at x = s*ex."""
    dx, dy, dz = px - s * ex, py - EYE_C.y, pz - EYE_C.z
    dxo = dx * s
    phi = np.arctan2(dxo, -dy)
    eps = np.arctan2(dz, np.sqrt(dx * dx + dy * dy))
    pm = np.where(phi > 0, PHI_OUT, PHI_IN) * eye
    a = np.abs(phi) / pm
    e = eps - (TILT + tilt) * phi
    up = e > 0
    b = np.abs(e) / np.where(up, EPS_UP * eye, EPS_LO * eye)
    rho = b + np.power(a, np.where(phi > 0, EXPO_OUT, EXPO_IN))
    return rho, a, b, phi, eps, up


def snap_scale(a, b, expo, iters=10):
    """k such that b/k + (a/k)^expo = 1 (the point scaled onto the lid margin)."""
    lo = np.full_like(a, 0.05)
    hi = np.full_like(a, 6.0)
    for _ in range(iters):
        k = np.sqrt(lo * hi)
        v = b / k + np.power(a / k, expo)
        big = v > 1.0
        lo = np.where(big, k, lo)
        hi = np.where(big, hi, k)
    return np.sqrt(lo * hi)


# ----------------------------------------------------------------------------- build
def build_head(P, name='head', carve=True, subdiv=7, verbose=False):
    """The head as a bmesh object (no material): see the module docstring. Eye openings are holes whose rims hug the eyeballs."""
    p = params(P.get('face', {}))
    t0 = time.time()
    sdf, sub = face_field(p)
    bm, D = sphere_dirs(subdiv)
    t = march(sdf, D)
    pos = f32(RAY_O) + D * t[:, None]
    if verbose:
        print('[face] marched %d rays in %.1fs' % (len(D), time.time() - t0), flush=True)
    keep_smooth = np.zeros(len(D), dtype=bool)
    ex = EYE_C.x * p['eye_gap']
    eye = p['eye']
    dead_faces = []
    carve = carve and int(p.get('stage', 9)) >= 5
    if carve:
        inside = np.zeros(len(D), dtype=bool)
        wgt_all = np.zeros(len(D), dtype=np.float32)
        for s in (-1, 1):
            c = f32([s * ex, EYE_C.y, EYE_C.z])
            d = pos - c
            ell = np.sqrt(d[:, 0] ** 2 + d[:, 2] ** 2)
            cand = (d[:, 1] < -0.002) & (ell < 0.040) & (pos[:, 0] * s > 0.006)
            idx = np.nonzero(cand)[0]
            if idx.size == 0:
                continue
            q = pos[idx]
            rho, a, b, phi, eps, up = window_rho(q[:, 0], q[:, 1], q[:, 2], s, ex, eye, p['eye_tilt'])
            # only skin that lies close to the eyeball takes part: towards the nose the lens of the opening reaches skin that is far from
            # the ball (the inner corner is a pocket), and pulling that onto the ball folds it into a black slit
            ln0 = np.sqrt((q[:, 0] - c[0]) ** 2 + (q[:, 1] - c[1]) ** 2 + (q[:, 2] - c[2]) ** 2)
            gate = 1.0 - sstep(EYE_R + 0.0120, EYE_R + 0.0180, ln0)
            # skin that faces the eye is pulled onto it, a wall seen edge-on (the side of the nose) would fold: it stays where it is
            nq = np.stack([sdf(q[:, 0] + 0.0006, q[:, 1], q[:, 2]) - sdf(q[:, 0] - 0.0006, q[:, 1], q[:, 2]),
                           sdf(q[:, 0], q[:, 1] + 0.0006, q[:, 2]) - sdf(q[:, 0], q[:, 1] - 0.0006, q[:, 2]),
                           sdf(q[:, 0], q[:, 1], q[:, 2] + 0.0006) - sdf(q[:, 0], q[:, 1], q[:, 2] - 0.0006)], axis=1)
            nq /= np.maximum(np.linalg.norm(nq, axis=1, keepdims=True), 1e-9)
            facing = np.abs(np.sum(nq * (q - c) / np.maximum(ln0, 1e-9)[:, None], axis=1))
            gate = gate * sstep(0.30, 0.70, facing)
            # snap the vertices close to the margin onto it so that the opening has a clean edge
            near = (rho > 0.84) & (rho < 1.16) & (gate > 0.5)
            k = np.where(near, snap_scale(a, b, np.where(phi > 0, EXPO_OUT, EXPO_IN)), 1.0)
            a2, b2 = a / k, b / k
            pm = np.where(phi > 0, PHI_OUT, PHI_IN) * eye
            phi2 = np.where(near, np.sign(phi) * a2 * pm, phi)
            ee = np.where(up, EPS_UP, EPS_LO) * eye
            e2 = np.where(near, np.sign(eps - (TILT + p['eye_tilt']) * phi) * b2 * ee, eps - (TILT + p['eye_tilt']) * phi)
            eps2 = np.where(near, e2 + (TILT + p['eye_tilt']) * phi2, eps)
            ln = np.sqrt((q[:, 0] - c[0]) ** 2 + (q[:, 1] - c[1]) ** 2 + (q[:, 2] - c[2]) ** 2)
            nx = s * np.cos(eps2) * np.sin(phi2)
            ny = -np.cos(eps2) * np.cos(phi2)
            nz = np.sin(eps2)
            q2 = np.where(near[:, None], c + np.stack([nx, ny, nz], axis=1) * ln[:, None], q)
            rho = np.where(near, 1.0, rho)
            # the skin hugs the eyeball like a lid near the opening and relaxes to the face further away
            dq = q2 - c
            sh = c + dq / np.linalg.norm(dq, axis=1, keepdims=True) * (EYE_R + 0.0006)
            ellq = np.sqrt(dq[:, 0] ** 2 + dq[:, 2] ** 2)
            wgt = (1.0 - sstep(0.9, 1.9, rho)) * (1.0 - sstep(0.026, 0.036, ellq)) * sstep(0.006, 0.014, q2[:, 0] * s) * gate
            q3 = q2 + (sh - q2) * wgt[:, None]
            dq = q3 - c
            r = np.linalg.norm(dq, axis=1)
            q3 = np.where((r < EYE_R + 0.0006)[:, None], c + dq / r[:, None] * (EYE_R + 0.0006), q3)
            pos[idx] = q3
            inside[idx[(rho <= 1.0001) & (gate > 0.5)]] = True
            wgt_all[idx] = np.maximum(wgt_all[idx], wgt)
        keep_smooth = wgt_all > 0.01
    for v, (x, y, z) in zip(bm.verts, pos.tolist()):
        v.co = (x, y, z)
    bm.normal_update()
    if carve:
        for f in bm.faces:
            if all(inside[v.index] for v in f.verts):
                dead_faces.append(f)
        if verbose:         # faces turned inside out by the pull onto the eyeballs (seen from the front the head is convex here)
            bad = [f for f in bm.faces if f.index >= 0 and not all(inside[v.index] for v in f.verts) and
                   f.normal.dot(f.calc_center_median() - V(0.0, 0.03, 0.0)) < 0 and f.calc_center_median().y < -0.05 and abs(f.calc_center_median().z) < 0.03]
            print('[face] folded faces near the eyes:', len(bad), [tuple(round(c, 4) for c in f.calc_center_median()) for f in bad[:6]], flush=True)
        bmesh.ops.delete(bm, geom=dead_faces, context='FACES')
        # loose vertices left behind by the deletion
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.translate(bm, vec=sk.HEAD_C, verts=bm.verts)
    bm.verts.ensure_lookup_table()
    ob = bm_obj(bm, name)
    ob.data.shade_smooth()
    ob['face_params'] = str({k: round(float(v), 4) for k, v in p.items()})
    if verbose:
        print('[face] head built in %.1fs, %d verts' % (time.time() - t0, len(ob.data.vertices)), flush=True)
    return ob


def midline(ob, zs=None):
    """Front surface y at x = 0 (head-local) for a few heights: to compare a head with the landmark table."""
    tree = bvh_of(ob)
    out = []
    for z in zs or [0.100, 0.075, 0.050, 0.030, 0.020, 0.010, 0.0, -0.010, -0.020, -0.030, -0.040, -0.048, -0.056, -0.062, -0.070,
                    -0.0775, -0.085, -0.095, -0.105, -0.115, -0.122]:
        hit = tree.ray_cast(sk.HEAD_C + V(0.0, -0.3, z), V(0, 1, 0))
        out.append((z, None if hit[0] is None else round(hit[0].y - sk.HEAD_C.y, 4)))
    return out


# ----------------------------------------------------------------------------- skin masks
def vertex_array(ob):
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)


def paint(ob, P, kind='head', hairline=None):
    """Writes the masks the skin material reads, as three colour attributes on the object (vertex positions are world = head centre
    + head-local). fa: lips, mouth line, brow zone, lash line. fb: nostrils, eye sockets, flush, beard. fc: hairline tint.
    'hairline' is a function (x, y, z head-local arrays) -> 0..1 weight of the tint under the edge of the hair."""
    p = params(P.get('face', {}))
    sdf, sub = face_field(p)
    q = vertex_array(ob) - f32([sk.HEAD_C.x, sk.HEAD_C.y, sk.HEAD_C.z])
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    X = np.abs(x)
    n = len(x)
    fa = np.zeros((n, 4), dtype=np.float32)
    fb = np.zeros((n, 4), dtype=np.float32)
    fc = np.zeros((n, 4), dtype=np.float32)
    front = sstep(-0.040, -0.062, y)
    mw = p['mouth_w']
    if kind == 'head':
        dl = np.minimum(sub['upper_lip'](X, y, z), sub['lower_lip'](X, y, z))
        lips = (1.0 - sstep(-0.0006, 0.0016, dl)) * front
        fa[:, 0] = lips
        gm_ = sd_chain(X, y, z, lip_chain(p), [0.0016] * 7)
        fa[:, 1] = np.exp(-0.5 * (gm_ / 0.0009) ** 2) * front * sstep(0.0, 0.0030, 0.0240 * mw - X)
        pts, rad = brow_curve(p)
        bd_ = dist2d(X, z, [(a[0], a[2]) for a in pts])
        wid = np.interp(np.clip((X - pts[0][0]) / (pts[-1][0] - pts[0][0] + 1e-6), 0, 1), np.linspace(0, 1, len(rad)), rad)
        fa[:, 2] = (1.0 - sstep(wid * 0.55, wid * 1.15, bd_)) * front * sstep(pts[0][0] - 0.004, pts[0][0] + 0.002, X)
        # eye margin: lashes along the upper rim, a fainter line along the lower one
        ex = EYE_C.x * p['eye_gap']
        lash = np.zeros(n, dtype=np.float32)
        socket = np.zeros(n, dtype=np.float32)
        lidtint = np.zeros(n, dtype=np.float32)
        for s in (-1, 1):
            side = (x * s) > 0
            rho, a, b, phi, eps, up = window_rho(x, y, z, s, ex, p['eye'], p['eye_tilt'])
            near = (1.0 - sstep(0.0, 0.30, np.abs(rho - 1.0))) * side * (y < -0.060)
            lash = np.maximum(lash, near * np.where(up, 1.0, 0.45))
            ell = np.sqrt((x - s * ex) ** 2 + (z - EYE_C.z) ** 2)
            sock = (1.0 - sstep(0.010, 0.030, ell)) * side * front
            socket = np.maximum(socket, sock * np.where(z > EYE_C.z, 1.0, 0.65))
            lidtint = np.maximum(lidtint, (1.0 - sstep(0.012, 0.020, ell)) * side * front * (z > EYE_C.z - 0.002) * (rho > 1.0))
        fa[:, 3] = lash
        fb[:, 1] = socket
        fc[:, 1] = lidtint
        dn = sub['nostril'](X, y, z)
        fb[:, 0] = (1.0 - sstep(-0.0004, 0.0016, dn)) * (y < -0.080) * (z < -0.045)
        # flush: cheeks, nose tip, a little on the chin and brow
        tipy = -0.0925 - 0.0075 * p['nose_h'] * p['nose_len']
        tipz = -0.0455 - 0.0020 * p['nose_len']
        cheeks = np.exp(-0.5 * (((X - 0.040) / 0.020) ** 2 + ((z + 0.040) / 0.020) ** 2)) * front
        tip = np.exp(-0.5 * (((X) / 0.011) ** 2 + ((z - tipz) / 0.011) ** 2 + ((y - tipy) / 0.020) ** 2))
        fb[:, 2] = np.clip(cheeks * 0.9 + tip * 1.0, 0, 1)
        # beard zone: upper lip, cheeks below the cheekbone, chin and jaw, fading down the neck
        ztop = -0.060 + 0.040 * sstep(0.0, 0.075, X)
        beard = sstep(ztop + 0.010, ztop - 0.012, z) * sstep(0.040, -0.030, y)
        fb[:, 3] = np.clip(beard, 0, 1) * (1.0 - np.clip(lips * 1.2, 0, 1))
    elif kind == 'neck':
        beard = (1.0 - sstep(-0.120, -0.175, z)) * sstep(0.030, -0.020, y)
        fb[:, 3] = beard
        fb[:, 2] = 0.15
    elif kind == 'ears':
        fb[:, 2] = 0.75
    if hairline is not None:
        fc[:, 0] = hairline(x, y, z)
    for name, arr in (('fa', fa), ('fb', fb), ('fc', fc)):
        arr[:, 3] = arr[:, 3] if name == 'fa' else arr[:, 3]
        at = ob.data.color_attributes.new(name, 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set('color', arr.ravel())


# ----------------------------------------------------------------------------- ears and eyes
def build_ears(P, name='ears', subdiv=5):
    """Both ears: a plate with a helix rim, a concha, a tragus and a lobe, from one signed-distance field (left ear, mirrored)."""
    p = params(P.get('face', {}))
    w = p['width']
    ax = 0.0700 * w
    c = (ax + 0.0030, 0.018, -0.013)

    def sdf(x, y, z):
        # plate of the ear leaning outwards, rim along its edge, bowl in the middle, lobe
        m = rotm(0, 0, -14)
        d = sd_ell(x, y, z, (ax + 0.0030, 0.018, -0.013), (0.0048, 0.0185, 0.0300), m)
        rim = sd_chain(x, y, z, [(ax + 0.0040, 0.0100, -0.0075), (ax + 0.0058, 0.0140, 0.0140), (ax + 0.0065, 0.0230, 0.0150),
                                 (ax + 0.0062, 0.0340, 0.0020), (ax + 0.0052, 0.0335, -0.0160)], [0.0030, 0.0035, 0.0038, 0.0034, 0.0028])
        d = smin(d, rim, 0.004)
        d = smin(d, sd_ell(x, y, z, (ax + 0.0040, 0.0150, -0.0390), (0.0062, 0.0105, 0.0105), m), 0.006)    # lobe
        d = smin(d, sd_ell(x, y, z, (ax + 0.0070, 0.0050, -0.0090), (0.0035, 0.0045, 0.0060)), 0.003)       # tragus
        d = smax(d, -sd_ell(x, y, z, (ax + 0.0125, 0.0165, -0.0070), (0.0080, 0.0110, 0.0165), m), 0.004)   # concha
        return d

    bm, D = sphere_dirs(subdiv)
    o = (ax + 0.0006, 0.019, -0.012)
    t = march(sdf, D, o=o, t0=0.002, t1=0.060, steps=40)
    pos = f32(o) + D * t[:, None]
    for v, (x, y, z) in zip(bm.verts, pos.tolist()):
        v.co = (x, y, z)
    bmesh.ops.translate(bm, vec=sk.HEAD_C, verts=bm.verts)
    right = bm.copy()
    for v in right.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(right, faces=right.faces)
    mesh = bmesh.new()
    off = len(bm.verts)
    for src in (bm, right):
        vm = {v: mesh.verts.new(v.co) for v in src.verts}
        for f in src.faces:
            mesh.faces.new([vm[v] for v in f.verts])
    bm.free()
    right.free()
    ob = bm_obj(mesh, name)
    ob.data.shade_smooth()
    return ob


def build_eyes(P, name='eyes'):
    """Two eyeballs (and the plugs of their corners); the front pole of the UV sphere looks along -y (the iris is painted there by the material)."""
    p = params(P.get('face', {}))
    c = sk.HEAD_C
    ex = EYE_C.x * p['eye_gap']
    obs = []
    for s in (-1, 1):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=20, radius=EYE_R)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(radians(90), 3, 'X'))
        ob = bm_obj(bm, f'eye_{"L" if s > 0 else "R"}')
        ob.location = c + V(s * ex, EYE_C.y, EYE_C.z)
        ob.data.shade_smooth()
        obs.append(ob)
        # the inner corner of each opening: a small plug behind it, in the colour of the rim of the eyeball (the same planar UV), so
        # that nobody sees the inside of the head through the gap between the ball and the skin; it reads as the caruncle
        bm = bmesh.new()
        bm_ellipsoid(bm, (-s * 0.0118, -0.0058, -0.0010), (0.0042, 0.0048, 0.0050), None, 12)
        ob = bm_obj(bm, f'eye_{"L" if s > 0 else "R"}c')
        ob.location = c + V(s * ex, EYE_C.y, EYE_C.z)
        ob.data.shade_smooth()
        obs.append(ob)
    return obs
