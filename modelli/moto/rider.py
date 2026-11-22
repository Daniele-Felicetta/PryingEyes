"""
The rider of the track mode: a man of 1.76 m in racing leathers, crouched on the bike (Blender 4.5, headless).

Everything is modelled in the pose of the bike at rest (the frame of geom.py), in the rest pose the game starts from. The game then moves the
pieces: the torso and the pelvis rigid from the hips, the helmet rigid from the neck, the arms and the legs skinned over two bones each
(shoulder-elbow, elbow-wrist; hip-knee, knee-ankle) with the elbow and the knee worked out every frame, the gloves on the grips, the boots on
the pegs. The joints the game needs are exported as empties (J_*).

Parts and materials (the role of a material is what the game reads: it picks the shader, the colours and the part of the livery):
  torso      suit            the pelvis, the trunk, the shoulders and the collar
  hump       suit2           the aerodynamic hump on the back
  arm_*      suit, suit2     two parts: the upper arm and the forearm (v runs 0..1 along each part, u round it, u = 0.25 on the outside), and the elbow cup in the second
  leg_*      suit2, suit2    the thigh and the shin, the knee cup (in the second) and the knee slider (the role slider: plastic)
  glove_*    glove           the fist round the grip and the cuff
  boot_*     boot, seal, slider   the boot on the peg: the foot, the shaft and its straps; the rubber sole; the plastic shell on the ankle
  helmet     helmet, seal, visor
"""
from geom import *

TORSO_PITCH = 28.0                  # degrees above the horizontal, at rest
HIP_C = V(0.0, -0.34, 0.945)        # the middle of the pelvis
HIP_X = 0.088                       # the hip joints, left and right
TORSO_LEN = 0.50                    # from the hips to the shoulder line, along the back
SHOULDER_X = 0.175
THIGH, SHIN = 0.43, 0.42
UPPER, FORE = 0.30, 0.27            # shoulder to elbow, elbow to wrist
ELBOW_OUT = 0.20                    # how far to the side the elbow bends (the pole of the arm, with 0.05 forward and 0.55 down: PistaRiderModel.cs has the same numbers). 0.42 put the elbows 0.36 m from the middle,
                                    # wider than the grips: the wings of a puppet; 0.20 tucks them (0.27 m), where a rider keeps them against the tank


def joints(naked=False):
    p = radians(TORSO_PITCH)
    u = V(0, cos(p), sin(p))                 # along the back, toward the head
    n = V(0, -sin(p), cos(p))                # out of the back
    J = {'u': u, 'n': n, 'hip_c': HIP_C}
    J['shoulder_c'] = HIP_C + u * TORSO_LEN
    J['neck'] = J['shoulder_c'] + n * 0.045 + u * 0.01
    J['head'] = J['neck'] + V(0, 0.115, 0.065)                 # the middle of the helmet
    grip = GRIP_NAKED if naked else GRIP_FAIRING
    for sd, s in ((1, 'R'), (-1, 'L')):
        J['hip_' + s] = V(sd * HIP_X, HIP_C.y + 0.01, HIP_C.z - 0.012)
        J['shoulder_' + s] = J['shoulder_c'] + V(sd * SHOULDER_X, 0, 0)
        g = V(sd * grip.x, grip.y, grip.z)
        J['grip_' + s] = g
        J['wrist_' + s] = g + V(-sd * 0.012, -0.068, 0.046)
        J['elbow_' + s], _ = two_bone(J['shoulder_' + s], J['wrist_' + s], UPPER, FORE, V(sd * ELBOW_OUT, 0.05, -0.55))
        peg = V(sd * PEG.x, PEG.y, PEG.z)
        J['peg_' + s] = peg
        J['ankle_' + s] = peg + V(0, -0.13, 0.095)
        J['knee_' + s], _ = two_bone(J['hip_' + s], J['ankle_' + s], THIGH, SHIN, V(0.0, 1.0, 0.15))
    return J


def _rot():
    return Euler((radians(TORSO_PITCH), 0, 0)).to_matrix()           # local y -> along the back, local z -> out of the back


# ----------------------------------------------------------------------------- the trunk
# sections along the back: t from the hip centre, half width, extent out of the back, extent toward the belly, exponent
TORSO = [
    (-0.150, 0.080, 0.060, 0.060, 2.4),
    (-0.090, 0.150, 0.095, 0.100, 2.5),
    (-0.020, 0.172, 0.115, 0.115, 2.5),         # the pelvis
    (0.060, 0.172, 0.118, 0.112, 2.5),
    (0.140, 0.157, 0.108, 0.100, 2.5),          # the waist
    (0.220, 0.160, 0.108, 0.105, 2.5),
    (0.300, 0.178, 0.116, 0.115, 2.5),
    (0.380, 0.193, 0.121, 0.120, 2.5),          # the ribs
    (0.450, 0.205, 0.113, 0.105, 2.6),
    (0.500, 0.212, 0.096, 0.088, 2.7),          # the shoulders
    (0.540, 0.150, 0.072, 0.066, 2.5),          # the slope of the neck
    (0.580, 0.078, 0.060, 0.060, 2.3),
    (0.610, 0.062, 0.058, 0.058, 2.2),
]


def torso_params(t):
    ts = [r[0] for r in TORSO]
    t = clamp(t, ts[0], ts[-1])
    i = 0
    while i < len(ts) - 2 and t > ts[i + 1]:
        i += 1
    f = (t - ts[i]) / (ts[i + 1] - ts[i])
    f = f * f * (3 - 2 * f)
    a, b = TORSO[i], TORSO[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in (1, 2, 3, 4))


def torso_ring(J, t, n=36, grow=0.0):
    hc, u, nn = J['hip_c'], J['u'], J['n']
    hw, back, front, p = torso_params(t)
    e = 2.0 / p
    out = []
    for j in range(n):
        th = 2 * pi * j / n
        c, s = cos(th), sin(th)
        x = (hw + grow) * math.copysign(abs(c) ** e, c)
        w = ((back if s > 0 else front) + grow) * math.copysign(abs(s) ** e, s)
        out.append(hc + u * t + nn * w + V(x, 0, 0))
    return out


def build_torso(J):
    rot = _rot()
    hc, u, n = J['hip_c'], J['u'], J['n']
    ts = [TORSO[0][0] + (TORSO[-1][0] - TORSO[0][0]) * k / 40.0 for k in range(41)]

    def fill(bm):
        loft3d([torso_ring(J, t) for t in ts], cap_start=True, cap_end=True, bm=bm)
        for sd in (-1, 1):
            bm_ellipsoid(bm, V(sd * 0.092, hc.y - 0.030, hc.z - 0.040), (0.098, 0.112, 0.092), None, 22)       # the buttocks and the tops of the thighs
            s = J['shoulder_' + ('R' if sd > 0 else 'L')]
            bm_ellipsoid(bm, s + V(sd * 0.002, 0.0, 0.004), (0.052, 0.058, 0.055), None, 20)                     # the caps of the shoulders (inside the top of the sleeve: a ball that shows is a toy's joint)
        bm_capsule(bm, J['shoulder_c'] + n * 0.02, J['neck'] + V(0, 0.03, 0.045), 0.062, 0.057, 16)              # the neck, up to under the helmet
    ob = organic('torso', fill, 0.008, sm_iter=10, sm_fac=0.5)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(ob.data)
    bm.free()
    return ob


def build_hump(J):
    hc, u, n = J['hip_c'], J['u'], J['n']
    rings = []
    for k in range(15):
        f = k / 14.0
        t = 0.12 + 0.46 * f
        h = 0.056 * math.sin(pi * f ** 0.85) ** 1.2
        wx = 0.034 + 0.062 * math.sin(pi * f ** 0.8) ** 0.7
        back = torso_params(t)[1]
        base, top = back - 0.020, back + h
        pts = superellipse(wx, (top - base) / 2 + 1e-4, 2.6, 24)
        rings.append([hc + u * t + n * ((top + base) / 2 + yy) + V(xx, 0, 0) for xx, yy in pts])
    bm = loft3d(rings, cap_start=True, cap_end=True)
    orient_outward(bm, hc + u * 0.35)
    return new_object(bm, 'hump')


# ----------------------------------------------------------------------------- protectors: plates and domes that hug a joint
def hug_pad(C, R, n, a, ea, eb, layers, p=2.6, seg=20, mat=0):
    """A plate wrapped on the sphere of radius R round the joint C, centred where the direction n meets it: ea long along a (projected on the
    surface), eb across. layers runs from the bottom up, (height above the sphere, scale of the outline): the first ones are buried in the limb so
    that no edge floats where the limb falls away, the last is the top face (closed). A sphere round the joint lies inside a bent tube of the same
    radius, so the plate never hangs in the air. Returns the bmesh."""
    n = Vector(n).normalized()
    a = Vector(a)
    a = (a - n * a.dot(n)).normalized()
    b = n.cross(a)
    rings = []
    for off, sc in layers:
        ring = []
        for x, y in superellipse(ea * sc, eb * sc, p, seg):
            d = sqrt(x * x + y * y)
            if d < 1e-9:
                dirv = n
            else:
                rho = d / R
                dirv = n * cos(rho) + (a * x + b * y) / d * sin(rho)
            ring.append(C + dirv * (R + off))
        rings.append(ring)
    bm = loft3d(rings, cap_start=False, cap_end=True)
    orient_outward(bm, C)
    for f in bm.faces:
        f.material_index = mat
    return bm


# a dome (the knee cup, the elbow cup): buried edge, a soft shoulder, a rounded top
DOME = [(-0.020, 1.00), (0.000, 1.00), (0.0050, 0.975), (0.0095, 0.90), (0.0130, 0.72), (0.0146, 0.42), (0.0150, 0.16)]
# a puck (the knee slider): a flat top with a chamfered edge
PUCK = [(-0.020, 1.00), (0.000, 1.00), (0.0050, 0.990), (0.0092, 0.935), (0.0104, 0.82), (0.0104, 0.50), (0.0104, 0.18)]


def scaled(layers, k, base=0.0):
    """The layers of a pad k times as high (the heights above the surface; the buried ones stay buried), lifted by 'base'."""
    return [(h * k + base if h > 0 else h, sc) for h, sc in layers]


# ----------------------------------------------------------------------------- arms and legs (the game skins them over two bones)
def build_arm(J, sd):
    """The sleeve (two parts, the upper arm and the forearm) and the elbow protector (in the material of the forearm). A dome on the shoulder as well made a ball joint of it: the torso's cap is enough."""
    s = 'R' if sd > 0 else 'L'
    S, E, W = J['shoulder_' + s], J['elbow_' + s], J['wrist_' + s]
    S0 = S - (E - S).normalized() * 0.045                        # the sleeve starts inside the shoulder, so that no flat end is left outside it
    pts = [S0, S, S.lerp(E, 0.5), E, E.lerp(W, 0.30), E.lerp(W, 0.72), W]
    radii = [0.050, 0.059, 0.052, 0.045, 0.046, 0.038, 0.032]
    ratio = [1.0, 1.0, 1.0, 1.04, 1.02, 0.96, 0.92]
    bm = tube(pts, radii, sides=20, ratio=ratio, ref=V(sd, 0, 0), seg=6, split=3)
    arm = new_object(bm, 'arm_' + s)
    # the elbow cup: on the outside of the bend (away from the shoulder and the wrist), long along the arm
    ne = -((S - E).normalized() + (W - E).normalized()).normalized()
    cup = hug_pad(E, 0.046, ne, W - S, 0.050, 0.041, scaled(DOME, 1.0), mat=1)
    elbow = new_object(cup, 'arm_elbow_' + s)
    return [arm, elbow]


def build_leg(J, sd):
    """The leg (the thigh and the shin), the knee cup in front of the knee and the slider on the outside of it. Returns the objects."""
    s = 'R' if sd > 0 else 'L'
    H, K, A = J['hip_' + s], J['knee_' + s], J['ankle_' + s]
    H0 = H - (K - H).normalized() * 0.07                           # the thigh starts inside the pelvis (a flat end under the hump showed as a cut)
    pts = [H0, H, H.lerp(K, 0.38), H.lerp(K, 0.72), K, K.lerp(A, 0.28), K.lerp(A, 0.68), A]
    radii = [0.088, 0.102, 0.095, 0.079, 0.067, 0.071, 0.057, 0.047]
    ratio = [1.0, 1.0, 0.97, 0.95, 0.95, 0.98, 1.0, 1.0]
    bm = tube(pts, radii, sides=22, ratio=ratio, ref=V(sd, 0, 0), seg=6, split=4)
    leg = new_object(bm, 'leg_' + s)
    up = V(0.0, -0.25, 1.0)
    # the cup over the knee: the kneecap points forward (the bisector of the bend, away from the hip and the ankle), a little to the outside
    nk = -((H - K).normalized() + (A - K).normalized()).normalized() + V(sd * 0.30, 0, 0)
    cup = hug_pad(K, 0.067, nk, up, 0.062, 0.046, scaled(DOME, 1.2), mat=1)
    knee = new_object(cup, 'leg_knee_' + s)
    # the slider: a puck on the outside of the knee cup
    puck = hug_pad(K, 0.067, V(sd * 0.99, 0.10, -0.14), up, 0.048, 0.040, scaled(PUCK, 1.0, 0.004))
    slider = new_object(puck, 'slider_' + s)
    return [leg, knee, slider]


# ----------------------------------------------------------------------------- hands and feet
def build_glove(J, sd):
    s = 'R' if sd > 0 else 'L'
    G = J['grip_' + s]
    W = J['wrist_' + s]
    E = J['elbow_' + s]
    fd = (W - E).normalized()            # the forearm

    def fill(bm):
        bm_ellipsoid(bm, G + V(0, -0.026, 0.030), (0.048, 0.047, 0.026), Matrix.Rotation(radians(-12), 3, 'X'), 20)       # the palm
        for i, dx in enumerate((-0.033, -0.011, 0.011, 0.031)):                 # index .. little finger, the index nearest to the middle of the bike
            x = G.x - sd * dx
            pts = [V(x, G.y + 0.000, G.z + 0.033), V(x, G.y + 0.027, G.z + 0.026), V(x, G.y + 0.039, G.z + 0.004), V(x, G.y + 0.031, G.z - 0.021)]
            bm_chain(bm, pts, (0.0108, 0.0100, 0.0094, 0.0086), 10)
        th = [V(G.x - sd * 0.042, G.y - 0.028, G.z + 0.015), V(G.x - sd * 0.053, G.y + 0.004, G.z - 0.006), V(G.x - sd * 0.044, G.y + 0.031, G.z - 0.012)]
        bm_chain(bm, th, (0.0135, 0.0118, 0.0102), 10)
        bm_chain(bm, [W - fd * 0.080, W + fd * 0.006], (0.048, 0.043), 16)                                          # the cuff
        bm_ellipsoid(bm, G + V(0, 0.003, 0.040), (0.043, 0.028, 0.013), None, 14)                                   # the knuckle guard
    return organic('glove_' + s, fill, 0.0045, sm_iter=4, sm_fac=0.4)


# the foot of the boot, from the back of the heel (t = 0) to the tip of the toe (t = 1): half width, height of the top above the underside of the sole, exponent
FOOT = [
    (0.00, 0.030, 0.062, 2.3),
    (0.04, 0.043, 0.090, 2.4),          # the heel counter
    (0.12, 0.049, 0.110, 2.5),
    (0.26, 0.050, 0.104, 2.5),          # the ankle (the shaft rises from here)
    (0.42, 0.051, 0.082, 2.5),          # the instep
    (0.58, 0.054, 0.066, 2.6),
    (0.72, 0.057, 0.055, 2.7),          # the ball of the foot
    (0.88, 0.0535, 0.044, 2.6),         # the toe box
    (0.94, 0.0475, 0.039, 2.4),
    (0.98, 0.0350, 0.033, 2.2),
    (1.00, 0.0150, 0.024, 2.0),
]


def table_at(tab, t):
    """The columns after the first of a table at t (the first column), a Catmull-Rom through the rows."""
    ts = [r[0] for r in tab]
    t = clamp(t, ts[0], ts[-1])
    i = 0
    while i < len(ts) - 2 and t > ts[i + 1]:
        i += 1
    f = (t - ts[i]) / (ts[i + 1] - ts[i])
    last = len(tab) - 1
    out = []
    for c in range(1, len(tab[0])):
        p0, p1, p2, p3 = tab[max(i - 1, 0)][c], tab[i][c], tab[i + 1][c], tab[min(i + 2, last)][c]
        out.append(0.5 * (2 * p1 + (p2 - p0) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f + (3 * p1 - p0 - 3 * p2 + p3) * f ** 3))
    return tuple(out)


def build_boot(J, sd):
    """The racing boot on the peg: the foot (a loft from the heel to the toe), the sole (rubber, a slab with a heel block, wider than the foot), the
    shaft up the shin with a band at its top and a strap, and the plastic shell on the outside of the ankle. Returns the objects."""
    s = 'R' if sd > 0 else 'L'
    A, K, peg = J['ankle_' + s], J['knee_' + s], J['peg_' + s]
    up = (K - A).normalized()
    x0 = A.x
    z0 = peg.z + 0.009                              # the underside of the sole, on the peg
    yh = A.y - 0.078                                # the back of the heel
    length = 0.318
    sole_top = 0.024

    # the foot
    ts = [0.5 - 0.5 * cos(pi * k / 15.0) for k in range(16)]
    rings = []
    for t in ts:
        hw, top, p = table_at(FOOT, t)
        zb = z0 + sole_top - 0.008
        zt = z0 + top
        pts = superellipse(hw, (zt - zb) / 2.0, p, 22)
        rings.append([V(x0 + sd * 0.002 + px, yh + t * length, (zt + zb) / 2.0 + pz) for px, pz in pts])
    bm = loft3d(rings, cap_start=True, cap_end=True)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    foot = new_object(bm, 'boot_' + s)

    # the shaft: from just under the ankle up the shin, flaring a little toward the calf, with a flat band at the top and a strap lower down
    P0, P1, P2 = A - up * 0.056, A + up * 0.070, A + up * 0.195
    shaft = new_object(tube([P0, P1, P2], (0.055, 0.057, 0.067), sides=22, ratio=(0.95, 0.95, 0.95), ref=V(sd, 0, 0), seg=5), 'boot_shaft_' + s)
    rim = new_object(tube([A + up * 0.181, A + up * 0.199], (0.0698, 0.0698), sides=22, ratio=(0.95, 0.95), ref=V(sd, 0, 0), seg=2), 'boot_rim_' + s)
    strap = new_object(tube([A + up * 0.094, A + up * 0.120], (0.0625, 0.0625), sides=22, ratio=(0.95, 0.95), ref=V(sd, 0, 0), seg=2), 'boot_strap_' + s)

    # the sole: the same plan as the foot, a little wider, the heel a block 34 mm high
    rings = []
    for t in [0.5 - 0.5 * cos(pi * k / 13.0) for k in range(14)]:
        hw, top, p = table_at(FOOT, t)
        th = 0.034 if t < 0.30 else (0.034 - 0.010 * smoothstep(0.30, 0.45, t))
        pts = superellipse(hw + 0.004, th / 2.0, 3.2, 22)
        rings.append([V(x0 + sd * 0.002 + px, yh + t * length, z0 + th / 2.0 + pz) for px, pz in pts])
    bm = loft3d(rings, cap_start=True, cap_end=True)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    sole = new_object(bm, 'seal_sole_' + s)

    # the shell on the outside of the ankle: plastic, long along the shaft
    ankle = new_object(hug_pad(A + up * 0.012, 0.056, V(sd, 0.0, 0.12), up, 0.046, 0.036, scaled(DOME, 0.9), seg=20), 'slider_ankle_' + s)
    return [foot, shaft, rim, strap, sole, ankle]


# ----------------------------------------------------------------------------- the helmet
# slices from the neck opening up to the crown: z, the extent behind the middle, the extent in front of it, the half width, the exponent
HELMET = [
    (-0.140, 0.075, 0.115, 0.070, 2.4),
    (-0.120, 0.105, 0.160, 0.093, 2.5),
    (-0.095, 0.135, 0.173, 0.108, 2.6),         # the chin bar
    (-0.060, 0.155, 0.160, 0.116, 2.6),
    (-0.020, 0.170, 0.148, 0.122, 2.5),         # the eyes
    (0.020, 0.174, 0.140, 0.124, 2.5),
    (0.060, 0.168, 0.127, 0.120, 2.4),
    (0.095, 0.152, 0.102, 0.109, 2.3),
    (0.122, 0.122, 0.074, 0.090, 2.2),
    (0.140, 0.080, 0.042, 0.062, 2.1),
    (0.152, 0.030, 0.016, 0.026, 2.0),
    (0.1585, 0.012, 0.007, 0.011, 2.0),
]


def helmet_slice(z):
    """(back, front, half width, exponent) at height z, from the table (a Catmull-Rom through the rows: a smoothstep between them
    flattens the profile at every row and the shell shows a ring there)."""
    zs = [h[0] for h in HELMET]
    z = clamp(z, zs[0], zs[-1])
    i = 0
    while i < len(zs) - 2 and z > zs[i + 1]:
        i += 1
    t = (z - zs[i]) / (zs[i + 1] - zs[i])
    last = len(HELMET) - 1
    out = []
    for c in (1, 2, 3, 4):
        p0, p1, p2, p3 = HELMET[max(i - 1, 0)][c], HELMET[i][c], HELMET[i + 1][c], HELMET[min(i + 2, last)][c]
        out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    return tuple(out)


def helmet_point(z, th, grow=0.0):
    back, front, hw, p = helmet_slice(z)
    e = 2.0 / p
    c, s = cos(th), sin(th)
    x = (hw + grow) * math.copysign(abs(c) ** e, c)
    y = ((front + grow) if s > 0 else (back + grow)) * math.copysign(abs(s) ** e, s)
    return V(x, y, z)


def build_helmet(J):
    """Shell, visor, its seal and the small black parts, in the frame of the helmet (origin at the middle of the head). The caller moves
    them to the rider's head. Returns the objects by role."""
    zs = [HELMET[0][0] + (HELMET[-1][0] - HELMET[0][0]) * (0.5 - 0.5 * cos(pi * k / 43.0)) for k in range(44)]          # denser at the crown and at the chin
    n = 52
    rings = [[helmet_point(z, 2 * pi * j / n) for j in range(n)] for z in zs]
    bm = loft3d(rings, cap_start=True, cap_end=True)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    shell = new_object(bm, 'helmet_shell')

    # no rear spoiler: a small fin read as an ear or a pig's tail in every photograph, from the first version to a duck tail across the back
    def patch(name, phi, zlo, zhi, grow, nz=10, nth=26, sweep=0.0, drop=0.0, taper=0.0):
        """A patch on the shell round the front: phi the half angle, zlo..zhi the height. sweep lifts the lower edge toward the sides, drop lowers the upper edge
        toward the sides, taper shortens the lowest row (a visor is a wedge, not a box)."""
        rows = []
        for k in range(nz):
            f = k / (nz - 1)
            ph = phi * (1.0 - taper * (1.0 - f))
            row = []
            for j in range(nth):
                u = -1 + 2 * j / (nth - 1)
                lo = zlo + sweep * u * u
                hi = zhi - drop * u * u
                row.append(helmet_point(lo + (hi - lo) * f, pi / 2 + ph * u, grow))
            rows.append(row)
        b = loft3d(rows, closed=False)
        orient_outward(b, V(0, 0, 0.0))
        return new_object(b, name)
    seal = patch('helmet_seal', radians(75), -0.067, 0.059, 0.0022, sweep=0.026, drop=0.018, taper=0.10)
    visor = patch('helmet_visor', radians(69), -0.059, 0.051, 0.0050, sweep=0.026, drop=0.018, taper=0.10)

    # the black parts: the neck roll and the visor screws
    bm = bmesh.new()
    pk_bm_ring(bm, V(0, 0.020, -0.137), 0.074, 0.011)
    for sd in (-1, 1):
        x = helmet_point(0.020, 0.0)[0] * sd
        bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.011, radius2=0.011, depth=0.008,
                              matrix=Matrix.Translation(V(sd * (helmet_point(0.022, 0.0).x + 0.002), 0.040, 0.022)) @ Matrix.Rotation(radians(90), 4, 'Y'))
    # no vents on the chin bar: three round holes in a row looked like the nostrils of a pig, and a slot in the middle like a mouth
    black = new_object(bm, 'helmet_black')
    return {'shell': shell, 'seal': seal, 'visor': visor, 'black': black}


def pk_bm_ring(bm, centre, R, r, seg=44, rseg=8, squash=1.28):
    for i in range(seg):
        a0, a1 = 2 * pi * i / seg, 2 * pi * (i + 1) / seg
        ring0, ring1 = [], []
        for j in range(rseg):
            b = 2 * pi * j / rseg
            for ring, a in ((ring0, a0), (ring1, a1)):
                ring.append(bm.verts.new(centre + V((R + r * cos(b)) * cos(a), (R + r * cos(b)) * sin(a) * squash, r * sin(b))))
        for j in range(rseg):
            bm.faces.new((ring0[j], ring1[j], ring1[(j + 1) % rseg], ring0[(j + 1) % rseg]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))


# ----------------------------------------------------------------------------- the whole rider
def build_rider(naked=False):
    """Builds the rider in the scene. Returns (parts, J): parts maps a part name to its objects."""
    J = joints(naked)
    parts = {}
    parts['torso'] = [build_torso(J)]
    parts['hump'] = [build_hump(J)]
    for sd, s in ((1, 'R'), (-1, 'L')):
        parts['arm_' + s] = build_arm(J, sd)
        parts['leg_' + s] = build_leg(J, sd)
        parts['glove_' + s] = [build_glove(J, sd)]
        parts['boot_' + s] = build_boot(J, sd)
    h = build_helmet(J)
    for ob in h.values():                    # to the rider's head, level (the rider looks at the road ahead)
        ob.location = J['head']
    parts['helmet'] = [h['shell'], h['seal'], h['visor'], h['black']]
    return parts, J
