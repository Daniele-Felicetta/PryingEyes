"""
The bikes of the track mode (Blender 4.5, headless): the chassis that every style shares (wheels, fork, swing arm, frame, engine, exhaust) and the
bodywork of each style: 0 superbike, 1 supersport, 2 naked roadster.

A bike is a Gun of the weapons' hard-surface kit (modelli/armi/kit.py): primitives drawn in a part and a role. A part is an object of the file with its
origin at the pivot of the node of the game's rig that moves it, a role is a material name that the game turns into one of its own:

  part   pivot                moves with
  body   the origin           the whole bike (it leans and pitches): frame, engine, exhaust, the bodywork, the seat, the screen, the lights
  swing  the swing arm pivot  the swing arm (a little turn with the shock): the arm, the chain
  rear   the rear axle        the rear wheel (it spins): tyre, rim, disc, sprocket
  steer  the steering head    the fork, the bars, the front mudguard (they turn); the headlamp of the naked bike
  front  the front axle       the front wheel (it spins, and rises and falls with the fork): tyre, rim, discs

Roles: paint and paint2 (the livery), frame, metal, steel, titan, engine, black, carbon, seat, glass (the mirrors), smoke (the windscreen), gold, caliper, grip, chain, radiator, tyre, lens, taillight,
rim (the rim, the spokes and the hub: the livery's own finish), discF and discR (the brake discs, drilled by the shader between their radii).
Axes and constants are those of geom.py (x right, y forward, z up, metres); docs/engineering/track-models.md explains the pipeline.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from geom import *

K = load_armikit()


# ----------------------------------------------------------------------------- shapes
def spl(vals, t):
    """Catmull-Rom through a list of numbers, at the parameter t (0 .. len - 1)."""
    n = len(vals)
    i = max(0, min(int(t), n - 2))
    u = t - i
    p0, p1, p2, p3 = vals[max(i - 1, 0)], vals[i], vals[i + 1], vals[min(i + 2, n - 1)]
    return 0.5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (3 * p1 - p0 - 3 * p2 + p3) * u ** 3)


def rrsec(hw, hwt, zb, zt, rb, rt, seg=2):
    """A rounded trapezoid in (x, z), counter-clockwise: half width hw at the bottom and hwt at the top, the corners rounded with rb below and rt above and
    made of seg segments each (with 40 degrees of smoothing limit a 45-degree facet is a hard edge: the bodywork of a sport bike is flat panels and chamfers)."""
    hh = zt - zb
    rb = max(min(rb, hw - 1e-4, hh / 2 - 1e-4), 1e-4)
    rt = max(min(rt, hwt - 1e-4, hh / 2 - 1e-4), 1e-4)
    out = []
    for cx, cz, r, a0 in ((hwt - rt, zt - rt, rt, 0), (-(hwt - rt), zt - rt, rt, 90), (-(hw - rb), zb + rb, rb, 180), (hw - rb, zb + rb, rb, 270)):
        for k in range(seg + 1):
            a = radians(a0 + 90.0 * k / seg)
            out.append((cx + r * cos(a), cz + r * sin(a)))
    return out


def rr_rings(rows, per=3, seg=2):
    """rows: (y, hw, hwt, zb, zt, rb, rt), in ascending y. Smooth between the rows: [(y, section)] for Gun.loft."""
    cols = list(zip(*rows))
    out = []
    for k in range((len(rows) - 1) * per + 1):
        t = k / per
        v = [spl(c, t) for c in cols]
        zb, zt = v[3], max(v[4], v[3] + 0.006)
        hw = max(v[1], 0.003)
        out.append((v[0], rrsec(hw, clamp(v[2], 0.002, hw), zb, zt, max(v[5], 0.001), max(v[6], 0.001), seg)))
    return out

def rpoly(pts, radii, seg=2):
    """A convex polygon in (x, z), counter-clockwise, with every corner rounded by a quadratic arc of seg + 1 points: whatever the corners, the same number of points, so that
    the sections of a loft can be blended into each other."""
    n = len(pts)
    out = []
    for i in range(n):
        p = Vector(pts[i])
        a = Vector(pts[i - 1]) - p
        b = Vector(pts[(i + 1) % n]) - p
        la, lb = a.length, b.length
        a.normalize()
        b.normalize()
        th = acos(clamp(a.dot(b), -0.9999, 0.9999))
        d = min(radii[i] / max(math.tan(th / 2), 1e-4), 0.45 * la, 0.45 * lb)
        A, B = p + a * d, p + b * d
        for k in range(seg + 1):
            t = k / seg
            q = A * (1 - t) ** 2 + p * (2 * t * (1 - t)) + B * (t * t)
            out.append((q.x, q.y))
    return out


HEX_COLS = 10                                       # a row of a flank loft: (y, hwb, hwm, hwt, zb, zm, zt, rb, rm, rt)


def hex_outline(v, seg=2):
    """The section of a flank loft for the blended values v of a row: a bottom edge (half width hwb, height zb), a shoulder (hwm at zm: the widest place, where the side has
    its crease) and a top edge (hwt at zt); rb, rm and rt round the corners of the bottom, of the shoulder and of the top."""
    y, hwb, hwm, hwt, zb, zm, zt, rb, rm, rt = v
    zt = max(zt, zb + 0.012)
    zm = clamp(zm, zb + 0.004, zt - 0.004)
    hwm = max(hwm, 0.004)
    hwb, hwt = clamp(hwb, 0.002, hwm), clamp(hwt, 0.002, hwm)
    rb, rm, rt = max(rb, 0.001), max(rm, 0.001), max(rt, 0.001)
    return rpoly([(hwt, zt), (-hwt, zt), (-hwm, zm), (-hwb, zb), (hwb, zb), (hwm, zm)], [rt, rt, rm, rb, rb, rm], seg)


def hex_values(rows, t):
    return [spl([r[c] for r in rows], t) for c in range(HEX_COLS)]


def hex_rings(rows, per=3, seg=2):
    """rows: (y, hwb, hwm, hwt, zb, zm, zt, rb, rm, rt). Smooth between the rows (a Catmull-Rom of every column): [(y, section)] for Gun.loft."""
    out = []
    for k in range((len(rows) - 1) * per + 1):
        v = hex_values(rows, k / per)
        out.append((v[0], hex_outline(v, seg)))
    return out


def outline_at(rows, y):
    """The section [(x, z)] of a flank loft at the distance y (the one of the nearest sample of the blended rows)."""
    n = len(rows)
    steps = (n - 1) * 24
    best = min((abs(hex_values(rows, (n - 1) * k / steps)[0] - y), (n - 1) * k / steps) for k in range(steps + 1))[1]
    return hex_outline(hex_values(rows, best))


def flank_x(rows, y, z):
    """The x of the right flank of a flank loft at the distance y and the height z (the vents, the mirrors' stalks and the gills sit on it)."""
    pts = outline_at(rows, y)
    xs = []
    for i in range(len(pts)):
        (x0, z0), (x1, z1) = pts[i], pts[(i + 1) % len(pts)]
        if (z0 - z) * (z1 - z) <= 0 and z0 != z1:
            xs.append(x0 + (x1 - x0) * (z - z0) / (z1 - z0))
    return max(xs) if xs else 0.0


def flank(rows, y, z, sd):
    """Where a plate lies on the flank (side sd, +1 right, -1 left): (its centre, the Euler degrees that lay it on the surface)."""
    x = flank_x(rows, y, z)
    dxdy = (flank_x(rows, y + 0.03, z) - flank_x(rows, y - 0.03, z)) / 0.06
    dxdz = (flank_x(rows, y, z + 0.03) - flank_x(rows, y, z - 0.03)) / 0.06
    return V(sd * x, y, z), (0, sd * degrees(math.atan(dxdz)), sd * -degrees(math.atan(dxdy)))


def seam(g, rows, path, r=0.0026, lift=0.0009):
    """A panel joint on both flanks of a flank loft: a thin dark pipe along 'path' (control points (y, z)). It is sampled every 2 cm so that it keeps to the surface (the sag of a straight
    piece between two samples is a fraction of its radius) and lifted by 'lift' so that half of it stands proud: seen from a few metres a groove is a dark line a pixel or two wide."""
    dense = []
    for (y0, z0), (y1, z1) in zip(path[:-1], path[1:]):
        n = max(1, int(math.hypot(y1 - y0, z1 - z0) / 0.02))
        dense += [(y0 + (y1 - y0) * k / n, z0 + (z1 - z0) * k / n) for k in range(n)]
    dense.append(tuple(path[-1]))
    xs = [flank_x(rows, y, z) + lift for y, z in dense]
    for sd in (-1, 1):
        pipe(g, 'black', [V(sd * x, y, z) for x, (y, z) in zip(xs, dense)], r, n=6, seg=1)


def ring_seam(g, rows, y, r=0.0026, lift=0.0009):
    """A joint all the way round a section of a flank loft at the distance y (a nose cone, a cover): a closed pipe on the outline, pushed out by 'lift' from the middle of the section."""
    pts = outline_at(rows, y)
    cx = sum(p[0] for p in pts) / len(pts)
    cz = sum(p[1] for p in pts) / len(pts)
    ring = []
    for x, z in pts:
        d = math.hypot(x - cx, z - cz) or 1.0
        ring.append(V(x + (x - cx) / d * lift, y, z + (z - cz) / d * lift))
    pipe(g, 'black', ring + [ring[0]], r, n=6, seg=1, cap=False)


def fasteners(g, rows, path, step=0.085, r=0.0052):
    """The quarter-turn fasteners of a race fairing: low domes of steel along 'path' (control points (y, z)) on both flanks, one every 'step' metres of the line, laid on the surface."""
    pts = [path[0]]
    run = 0.0
    for (y0, z0), (y1, z1) in zip(path[:-1], path[1:]):
        L = math.hypot(y1 - y0, z1 - z0)
        t = step - run
        while t <= L:
            pts.append((y0 + (y1 - y0) * t / L, z0 + (z1 - z0) * t / L))
            t += step
        run = (run + L) % step
    for y, z in pts:
        for sd in (-1, 1):
            c, rot = flank(rows, y, z, sd)
            blob(g, 'metal', c, (0.0016, r, r), rot=rot, segs=10)


def arch(hw, zc, th, rise, m=9):
    """A thin curved plate in (x, z): thickness th, higher by 'rise' in the middle (a screen, a lens cover)."""
    top = [(hw * (1 - 2.0 * k / (m - 1)), zc + rise * (1 - (1 - 2.0 * k / (m - 1)) ** 2) + th / 2) for k in range(m)]
    bot = [(x, z - th) for x, z in reversed(top)]
    return top + bot


def pipe(g, role, pts, rx, rz=None, n=14, seg=6, cap=True, ref=V(1, 0, 0)):
    """A pipe through control points (a Catmull-Rom). rx is the radius along the direction 'ref' (the part of it that is square to the pipe: always the same side, so that
    the rings do not twist), rz the radius along the other axis; numbers or one per point."""
    rz = rx if rz is None else rz
    rx = rx if isinstance(rx, (list, tuple)) else [rx] * len(pts)
    rz = rz if isinstance(rz, (list, tuple)) else [rz] * len(pts)
    path, par = catmull(pts, seg)
    bm = g._bm(role)
    n0 = len(bm.faces)
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        s = ref - t * ref.dot(t)
        if s.length < 1e-3:
            s = V(0, 0, 1) - t * t.z
        s.normalize()
        u = s.cross(t)
        a, b = spl(rx, par[i]), spl(rz, par[i])
        rings.append([bm.verts.new(p + s * (a * cos(2 * pi * j / n)) + u * (b * sin(2 * pi * j / n))) for j in range(n)])
    for A, B in zip(rings[:-1], rings[1:]):
        for j in range(n):
            bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
    if cap:
        bm.faces.new(rings[0][::-1])
        bm.faces.new(rings[-1])
    g._fin(bm, n0)
    return g


def blob(g, role, c, r, squash=(1, 1, 1), rot=None, segs=16):
    """An ellipsoid (a headlamp, a mirror, a cap), optionally turned: rot is Euler degrees like the kit's."""
    bm = g._bm(role)
    n0 = len(bm.faces)
    vs = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(6, segs // 2), radius=1.0)['verts']
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(c)) @ K.rotm(rot) @ Matrix.Diagonal((r[0] * squash[0], r[1] * squash[1], r[2] * squash[2], 1.0)), verts=vs)
    g._fin(bm, n0)
    return g


def tangent_strip(c1, r1, c2, r2, upper):
    """The outer tangent of two circles (in y, z): the two points on circle 1 and on circle 2, on the upper or on the lower side (a chain run)."""
    d = c2 - c1
    L = d.length
    beta = math.asin(clamp((r2 - r1) / L, -1, 1))
    base = atan2(d.y, d.x)
    a = base + (pi / 2 + beta if upper else -pi / 2 - beta)
    n = Vector((cos(a), sin(a)))
    return c1 + n * r1, c2 + n * r2


# ----------------------------------------------------------------------------- wheels
def wheel(g, c, rear):
    """The tyre, the rim with its five spokes, the hub, the discs and (behind) the sprocket, round the axle c along x."""
    hw = 0.095 if rear else 0.060
    if rear:
        tp = [(-0.060, 0.222), (-0.074, 0.234), (-0.088, 0.258), (-0.095, 0.284), (-0.086, 0.300), (-0.062, 0.3085), (-0.030, 0.3098), (0.0, 0.31)]
    else:
        tp = [(-0.040, 0.222), (-0.050, 0.232), (-0.057, 0.250), (-0.060, 0.272), (-0.055, 0.292), (-0.042, 0.3045), (-0.022, 0.3092), (0.0, 0.31)]
    tp = tp + [(-x, r) for x, r in reversed(tp[:-1])]
    g.lathe('tyre', c, tp, segs=80, axis='x', loop=True)
    # the rim: the lips, the well, and the inside (a barrel, hidden by the spokes)
    w = 0.0675 if rear else 0.046
    rim = [(-w, 0.2255), (-w + 0.005, 0.2255), (-w + 0.009, 0.2175), (-w + 0.017, 0.2045), (-w + 0.033, 0.1995), (w - 0.033, 0.1995), (w - 0.017, 0.2045), (w - 0.009, 0.2175), (w - 0.005, 0.2255), (w, 0.2255),
           (w, 0.180), (-w, 0.180)]
    g.lathe('rim', c, rim, segs=72, axis='x', loop=True)                         # the rim, the spokes and the hub are the role 'rim': the game gives each livery its own finish (silver, black, gold)
    # five spokes: a star that narrows toward the middle and widens where it meets the rim
    spoke = [(-0.024, 0.030), (-0.017, 0.065), (-0.013, 0.105), (-0.016, 0.150), (-0.027, 0.196), (0.027, 0.196), (0.016, 0.150), (0.013, 0.105), (0.017, 0.065), (0.024, 0.030)]
    for k in range(5):
        a = radians(72 * k + 18)
        ca, sa = cos(a), sin(a)
        pts = [(c.y + u * ca - v * sa, c.z + u * sa + v * ca) for u, v in spoke]
        g.prism('rim', pts, c.x - 0.011, c.x + 0.011, r=0.0016)
    g.lathe('rim', c, [(-0.052 if not rear else -0.064, 0.0), (-0.052 if not rear else -0.064, 0.030), (0.052 if not rear else 0.064, 0.030), (0.052 if not rear else 0.064, 0.0)], segs=28, axis='x')
    g.lathe('metal', c, [(-0.066 if not rear else -0.078, 0.0), (-0.066 if not rear else -0.078, 0.017), (0.066 if not rear else 0.078, 0.017), (0.066 if not rear else 0.078, 0.0)], segs=18, axis='x')
    if not rear:
        for sd in (-1, 1):                                                       # two discs and their carriers: drilled (the role 'discF': PistaPaint part 31 cuts the holes, between the radii 0.112 and 0.165)
            g.lathe('discF', c + V(sd * 0.057, 0, 0), [(-0.0028, 0.112), (-0.0028, 0.165), (0.0028, 0.165), (0.0028, 0.112)], segs=72, axis='x', loop=True)
    else:
        g.lathe('discR', c + V(0.060, 0, 0), [(-0.0028, 0.082), (-0.0028, 0.130), (0.0028, 0.130), (0.0028, 0.082)], segs=64, axis='x', loop=True)      # (radii 0.082 and 0.130)
        g.lathe('metal', c + V(-0.106, 0, 0), [(-0.032, 0.0), (-0.032, 0.026), (0.030, 0.026), (0.030, 0.0)], segs=20, axis='x')                         # the spacer through the swing arm
        g.lathe('steel', c + V(-0.136, 0, 0), [(-0.0025, 0.046), (-0.0025, 0.087), (0.0025, 0.087), (0.0025, 0.046)], segs=44, axis='x', loop=True)      # the sprocket


# ----------------------------------------------------------------------------- the chassis
SPROCKET_F = V(-0.136, -0.190, 0.385)            # the output shaft of the engine, with the front sprocket of the chain
SPROCKET_F_R, SPROCKET_R_R = 0.040, 0.087


def fork_axis(s, side=0.0):
    """A point on the steering axis (or on a fork leg, 'side' metres to the right of it), s metres up from the front axle."""
    return AXLE_F + STEER_AXIS * s + V(side, 0, 0)


def fork(g, style):
    g.part('steer', pivot=HEAD)
    rake = degrees(RAKE)
    for sd in (-1, 1):
        side = sd * 0.090
        g.rod('gold', fork_axis(0.045, side), fork_axis(0.43, side), 0.0185, segs=14)                    # the slim inner tubes
        g.rod('black', fork_axis(0.385, side), fork_axis(0.715, side), 0.0245, segs=16)                  # the outer tubes, clamped in the yokes
        g.rod('gold', fork_axis(0.378, side), fork_axis(0.400, side), 0.0258, segs=16)                   # the ring of the seal
        g.box('metal', fork_axis(0.0, side + sd * 0.018), (0.034, 0.075, 0.082), r=0.007, rot=(rake, 0, 0))   # the axle clamp
        g.rod('metal', AXLE_F + V(sd * 0.064, 0, 0), AXLE_F + V(sd * 0.112, 0, 0), 0.0135, segs=14)       # the axle
        # the radial caliper on its lug, ahead of the leg, straddling the disc
        a = radians(40)
        rc = AXLE_F + V(sd * 0.057, cos(a) * 0.150, sin(a) * 0.150)
        g.box('caliper', rc, (0.046, 0.090, 0.050), r=0.008, rot=(-50, 0, 0))
        g.box('metal', rc + V(sd * 0.026, -0.030, -0.030), (0.024, 0.07, 0.03), r=0.004, rot=(-20, 0, 0))
        pipe(g, 'black', [rc + V(sd * 0.0, 0.0, 0.02), fork_axis(0.43, side * 1.05) + V(sd * 0.02, 0.05, 0.0), fork_axis(0.64, sd * 0.052) + V(0, 0.03, 0.0)], 0.0042, n=8)     # the brake line
    g.box('metal', fork_axis(0.700), (0.215, 0.066, 0.026), r=0.007, rot=(rake, 0, 0))                    # the upper yoke
    g.box('metal', fork_axis(0.590), (0.205, 0.072, 0.030), r=0.007, rot=(rake, 0, 0))                    # the lower yoke
    g.rod('metal', fork_axis(0.545), fork_axis(0.745), 0.021, segs=16)                                    # the steering stem and its nut
    g.rod('metal', fork_axis(0.712), fork_axis(0.724), 0.027, segs=16)
    # the front mudguard follows the wheel at a distance, and is wider in the middle
    pts = [(AXLE_F.y + 0.352 * cos(radians(a)), AXLE_F.z + 0.352 * sin(radians(a))) for a in range(16, 146, 10)]
    hws = [0.040, 0.056, 0.070, 0.080, 0.085, 0.086, 0.084, 0.080, 0.074, 0.066, 0.056, 0.046, 0.040, 0.034]
    g.sweep('paint2', pts, hws[:len(pts)], 0.0035, rad=0.002, seg=2)
    # the bars
    if style < 2:
        g_pts = lambda sd: [fork_axis(0.612, sd * 0.090), V(sd * 0.150, 0.448, 0.878), V(sd * 0.220, 0.428, 0.893), V(sd * 0.262, 0.404, 0.905)]
        bar_y, bar_z = GRIP_FAIRING.y, GRIP_FAIRING.z
    else:
        g_pts = lambda sd: [V(sd * 0.045, 0.404, 0.975), V(sd * 0.150, 0.386, 0.992), V(sd * 0.225, 0.356, 1.000), V(sd * 0.262, 0.340, 1.000)]
        bar_y, bar_z = GRIP_NAKED.y, GRIP_NAKED.z
    for sd in (-1, 1):
        pipe(g, 'metal', g_pts(sd), 0.0108, n=10, ref=V(0, 1, 0))
        g.rod('grip', V(sd * 0.240, bar_y, bar_z), V(sd * 0.362, bar_y, bar_z), 0.0178, segs=14)
        g.rod('metal', V(sd * 0.362, bar_y, bar_z), V(sd * 0.372, bar_y, bar_z), 0.0140, segs=12)             # the bar end
        g.box('black', V(sd * 0.192, bar_y + 0.030, bar_z + 0.030), (0.030, 0.040, 0.046), r=0.006)           # the reservoir of the brake and of the clutch
        g.box('metal', V(sd * 0.300, bar_y + 0.060, bar_z - 0.004), (0.012, 0.12, 0.007), r=0.002, rot=(0, 0, sd * -8))     # the lever
    if style == 2:                                                                                            # the round headlamp on its brackets, and its lens
        blob(g, 'black', V(0, 0.598, 0.972), (0.122, 0.095, 0.122), segs=24)
        blob(g, 'lens', V(0, 0.690, 0.972), (0.100, 0.040, 0.100), segs=24)
        for sd in (-1, 1):
            g.rod('metal', fork_axis(0.64, sd * 0.090), V(sd * 0.105, 0.610, 0.972), 0.008, segs=8)


def swing(g):
    g.part('swing', pivot=SWING_PIVOT)
    for sd in (-1, 1):
        x = sd * 0.106
        g.sweep('metal', [(-0.30, 0.450), (-0.38, 0.428), (-0.50, 0.388), (-0.62, 0.347), (-0.70, 0.318)], [0.021, 0.022, 0.021, 0.019, 0.017], [0.042, 0.038, 0.032, 0.027, 0.023], rad=0.006, seg=2, x=x)
        g.box('metal', V(sd * 0.130, -0.700, 0.310), (0.014, 0.080, 0.080), r=0.005)                      # the axle plate and its adjuster
        g.box('metal', V(sd * 0.145, -0.746, 0.310), (0.008, 0.026, 0.020), r=0.003)
    g.box('metal', V(0, -0.46, 0.402), (0.215, 0.040, 0.034), r=0.010)                                    # the cross member
    g.box('black', V(0, -0.34, 0.455), (0.20, 0.050, 0.030), r=0.008)                                     # the bridge to the shock linkage
    g.box('caliper', V(0.064, -0.585, 0.372), (0.040, 0.075, 0.050), r=0.007, rot=(10, 0, 0))             # the rear caliper
    # the chain, on the left, between the two sprockets: its two runs, and the sprockets' teeth are the discs of the wheels
    rc = Vector((AXLE_R.y, AXLE_R.z))
    fc = Vector((SPROCKET_F.y, SPROCKET_F.z))
    for upper in (True, False):
        a, b = tangent_strip(fc, SPROCKET_F_R, rc, SPROCKET_R_R, upper)              # 2D points (y, z)
        d = b - a
        mid = (a + b) / 2
        g.box('chain', V(-0.136, mid.x, mid.y), (0.014, d.length, 0.006), r=0.001, rot=(degrees(atan2(d.y, d.x)), 0, 0))
    g.lathe('steel', V(SPROCKET_F.x, SPROCKET_F.y, SPROCKET_F.z), [(-0.0025, 0.0), (-0.0025, SPROCKET_F_R), (0.0025, SPROCKET_F_R), (0.0025, 0.0)], segs=24, axis='x')


def frame(g, style):
    g.part('body', pivot=(0, 0, 0))
    g.rod('frame', fork_axis(0.600), fork_axis(0.695), 0.035, segs=18)                                       # the head tube
    for sd in (-1, 1):
        # the main spar, from the head to the plate of the swing arm pivot
        pipe(g, 'metal', [V(sd * 0.050, 0.405, 0.915), V(sd * 0.080, 0.290, 0.912), V(sd * 0.108, 0.080, 0.868), V(sd * 0.118, -0.110, 0.775), V(sd * 0.116, -0.245, 0.640), V(sd * 0.112, -0.290, 0.520)],
             [0.024, 0.029, 0.033, 0.033, 0.030, 0.026], [0.030, 0.036, 0.043, 0.043, 0.040, 0.032], n=12)
        g.prism('metal', [(-0.255, 0.640), (-0.340, 0.600), (-0.345, 0.455), (-0.300, 0.395), (-0.235, 0.440), (-0.215, 0.560)], sd * 0.108, sd * 0.124, r=0.004)      # the pivot plate
        pipe(g, 'frame', [V(sd * 0.075, -0.20, 0.800), V(sd * 0.080, -0.46, 0.835), V(sd * 0.068, -0.80, 0.885)], 0.0125, n=8)             # the sub frame under the seat
        pipe(g, 'frame', [V(sd * 0.110, -0.26, 0.610), V(sd * 0.085, -0.40, 0.745), V(sd * 0.080, -0.46, 0.835)], 0.0105, n=8)
        g.prism('metal', [(-0.150, 0.520), (-0.300, 0.520), (-0.315, 0.440), (-0.265, 0.372), (-0.170, 0.385)], sd * 0.112, sd * 0.121, r=0.003)          # the peg hanger
        g.rod('metal', V(sd * 0.115, PEG.y, PEG.z), V(sd * 0.290, PEG.y, PEG.z), 0.0088, segs=10)                                          # and the peg
        g.rod('metal', V(sd * 0.115, PEG.y - 0.14, PEG.z + 0.045), V(sd * 0.150, PEG.y - 0.14, PEG.z + 0.045), 0.0100, segs=10)               # the gear lever and the brake pedal pivot
    g.box('frame', V(0, 0.336, 0.470), (0.20, 0.030, 0.050), r=0.008)                                        # a cross member in front of the engine


def engine(g, style):
    g.part('body')
    g.box('engine', V(0, -0.02, 0.365), (0.235, 0.40, 0.165), r=0.025, seg=3)                               # the crankcase and the sump
    g.box('engine', V(0, -0.03, 0.285), (0.170, 0.30, 0.060), r=0.015)
    foot = V(0, 0.10, 0.47)
    ax = V(0, sin(radians(18)), cos(radians(18)))                                                            # the cylinders lean forward
    tilt = (-18, 0, 0)
    g.box('engine', foot + ax * 0.115, (0.215, 0.170, 0.180), r=0.008, rot=tilt)
    for i in range(5):                                                                                       # the cooling fins
        g.box('engine', foot + ax * (0.030 + 0.034 * i), (0.222, 0.178, 0.008), r=0.001, rot=tilt)
    g.box('engine', foot + ax * 0.238, (0.226, 0.190, 0.060), r=0.006, rot=tilt)                            # the head
    g.box('black', foot + ax * 0.292, (0.196, 0.166, 0.040), r=0.012, rot=tilt)                            # the cam cover
    g.lathe('metal', V(0.117, -0.09, 0.37), [(0.0, 0.0), (0.0, 0.080), (0.034, 0.080), (0.034, 0.0)], segs=28, axis='x')      # the clutch cover
    g.lathe('metal', V(-0.117, -0.12, 0.42), [(0.0, 0.0), (0.0, 0.065), (-0.030, 0.065), (-0.030, 0.0)], segs=26, axis='x')    # the alternator cover
    g.box('radiator', V(0, 0.385, 0.555), (0.235, 0.045, 0.27), r=0.006, rot=(-12, 0, 0))                    # the radiator
    g.rod('steel', V(-0.118, SPROCKET_F.y, SPROCKET_F.z), V(-0.142, SPROCKET_F.y, SPROCKET_F.z), 0.016, segs=14)             # the output shaft


def exhaust(g, style):
    g.part('body')
    for x in (-0.075, -0.026, 0.026, 0.075):                                                                 # four headers into one collector
        pipe(g, 'titan', [V(x, 0.205, 0.560), V(x * 1.15, 0.255, 0.470), V(x * 1.00, 0.250, 0.345), V(x * 0.55, 0.170, 0.262), V(x * 0.25, 0.040, 0.228), V(x * 0.10, -0.090, 0.232)], 0.0172, n=12)
    pipe(g, 'titan', [V(0.0, -0.09, 0.232), V(0.06, -0.17, 0.246), V(0.12, -0.26, 0.285)], [0.040, 0.046, 0.050], n=16)
    pipe(g, 'titan', [V(0.12, -0.26, 0.285), V(0.15, -0.38, 0.33), V(0.17, -0.60, 0.41), V(0.185, -0.82, 0.50)], [0.046, 0.053, 0.056, 0.052], n=18)      # the silencer
    g.lathe('carbon', V(0.185, -0.82, 0.50), [(-0.030, 0.0), (-0.030, 0.0535), (0.004, 0.0535), (0.004, 0.0)], segs=20, axis='y', rot=(157.7, 0, 0))   # its cap
    g.lathe('black', V(0.1855, -0.822, 0.5005), [(-0.030, 0.0), (-0.030, 0.046), (0.0075, 0.046), (0.0075, 0.0)], segs=20, axis='y', rot=(157.7, 0, 0))


# ----------------------------------------------------------------------------- the bodywork
# The fairing with its belly pan, one shell: rows (y, hwb, hwm, hwt, zb, zm, zt, rb, rm, rt) of a flank loft (hex_outline). Ahead of y = 0.40 the lower edge keeps clear of the front wheel and its
# mudguard (they rise and turn); behind the wheel it drops to the belly, whose bottom stays above the collector of the exhaust (z 0.28).
FAIR = [
    (0.895, 0.010, 0.012, 0.010, 0.722, 0.737, 0.752, 0.006, 0.006, 0.006),
    (0.850, 0.040, 0.056, 0.042, 0.700, 0.735, 0.772, 0.014, 0.012, 0.014),
    (0.780, 0.070, 0.104, 0.072, 0.680, 0.742, 0.808, 0.022, 0.030, 0.026),
    (0.690, 0.100, 0.150, 0.102, 0.662, 0.752, 0.862, 0.030, 0.050, 0.036),
    (0.580, 0.125, 0.190, 0.130, 0.630, 0.768, 0.918, 0.040, 0.070, 0.045),
    (0.470, 0.135, 0.214, 0.142, 0.575, 0.775, 0.962, 0.045, 0.080, 0.050),
    (0.360, 0.150, 0.228, 0.150, 0.450, 0.775, 0.992, 0.050, 0.080, 0.052),
    (0.240, 0.158, 0.234, 0.150, 0.340, 0.770, 1.000, 0.055, 0.080, 0.052),
    (0.120, 0.162, 0.226, 0.146, 0.300, 0.750, 0.972, 0.055, 0.080, 0.050),
    (0.020, 0.158, 0.200, 0.126, 0.285, 0.715, 0.926, 0.050, 0.070, 0.046),
    (-0.080, 0.130, 0.152, 0.092, 0.285, 0.680, 0.862, 0.040, 0.050, 0.040),
    (-0.180, 0.085, 0.100, 0.060, 0.305, 0.600, 0.790, 0.030, 0.035, 0.030),
    (-0.260, 0.035, 0.050, 0.030, 0.345, 0.510, 0.640, 0.020, 0.020, 0.020),
]
TANK = [
    (0.370, 0.030, 0.020, 0.935, 0.985, 0.010, 0.010),
    (0.300, 0.100, 0.075, 0.905, 1.012, 0.030, 0.030),
    (0.180, 0.152, 0.108, 0.872, 1.034, 0.040, 0.040),
    (0.040, 0.168, 0.112, 0.842, 1.030, 0.040, 0.040),
    (-0.100, 0.154, 0.105, 0.832, 1.004, 0.040, 0.040),
    (-0.220, 0.124, 0.088, 0.832, 0.968, 0.035, 0.035),
    (-0.320, 0.094, 0.070, 0.838, 0.936, 0.030, 0.030),
    (-0.370, 0.050, 0.040, 0.850, 0.915, 0.020, 0.020),
]
TANK_NAKED = [
    (0.410, 0.030, 0.020, 0.930, 0.990, 0.010, 0.010),
    (0.340, 0.115, 0.085, 0.890, 1.045, 0.035, 0.035),
    (0.200, 0.168, 0.118, 0.862, 1.068, 0.045, 0.045),
    (0.040, 0.180, 0.122, 0.840, 1.058, 0.045, 0.045),
    (-0.100, 0.162, 0.110, 0.832, 1.030, 0.040, 0.040),
    (-0.220, 0.128, 0.090, 0.832, 0.980, 0.035, 0.035),
    (-0.320, 0.094, 0.070, 0.838, 0.940, 0.030, 0.030),
    (-0.370, 0.050, 0.040, 0.850, 0.915, 0.020, 0.020),
]
SEAT = [
    (-0.050, 0.075, 0.060, 0.857, 0.890, 0.012, 0.012),
    (-0.140, 0.125, 0.100, 0.842, 0.905, 0.020, 0.020),
    (-0.300, 0.138, 0.108, 0.836, 0.918, 0.022, 0.022),
    (-0.440, 0.122, 0.098, 0.840, 0.914, 0.020, 0.020),
    (-0.520, 0.100, 0.082, 0.848, 0.906, 0.018, 0.018),
]
# The seat cowl: a box with a bevelled top, its sides nearly upright up to the shoulder, which is where the side number plates are (PistaBikeMesh puts them at x 0.1135, z 0.89, y -0.64).
TAIL = [
    (-0.400, 0.062, 0.074, 0.056, 0.792, 0.872, 0.905, 0.022, 0.020, 0.022),
    (-0.500, 0.084, 0.102, 0.068, 0.775, 0.890, 0.922, 0.030, 0.030, 0.030),
    (-0.620, 0.100, 0.116, 0.076, 0.770, 0.912, 0.946, 0.034, 0.032, 0.034),
    (-0.760, 0.088, 0.106, 0.066, 0.795, 0.945, 0.982, 0.030, 0.030, 0.032),
    (-0.880, 0.064, 0.080, 0.050, 0.838, 0.972, 1.006, 0.024, 0.026, 0.026),
    (-0.950, 0.040, 0.050, 0.032, 0.872, 0.980, 1.012, 0.016, 0.018, 0.018),
    (-0.985, 0.018, 0.024, 0.014, 0.905, 0.984, 1.002, 0.010, 0.010, 0.010),
]
SCREEN = [(0.470, 0.150, 0.985, 0.012), (0.390, 0.156, 1.046, 0.022), (0.310, 0.146, 1.106, 0.030), (0.230, 0.120, 1.166, 0.034), (0.190, 0.084, 1.196, 0.030)]
# The joints of the fairing: the line where the upper fairing (the vent, the gills) meets the lower one (it climbs toward the nose and runs level under the knee), as (y, z) on the flank, and the
# distance of the section where the nose cone meets the rest. The quarter-turn fasteners sit a finger above the first line.
LOWER_SEAM = [(0.60, 0.690), (0.52, 0.662), (0.42, 0.600), (0.30, 0.535), (0.16, 0.495), (0.00, 0.470), (-0.08, 0.470)]
NOSE_SEAM_Y = 0.60


def bodywork(g, style):
    g.part('body')
    fair = style < 2
    s = 0.97 if style == 1 else 1.0

    def scaled(rows):
        return [(y, hw * s, hwt * s, zb, zt, rb, rt) for (y, hw, hwt, zb, zt, rb, rt) in rows]

    def fscaled(rows):                                                                                           # the half widths of a flank loft (columns 1, 2, 3)
        return [(r[0], r[1] * s, r[2] * s, r[3] * s) + tuple(r[4:]) for r in rows]

    fr = fscaled(FAIR)
    if fair:
        g.loft('paint', hex_rings(fr), cap=True)
        g.part('screen', pivot=(0, 0, 0))                                                                         # the windscreen is a part of its own: the game hides it in the first-person view
        g.loft('smoke', [(y, arch(hw * s, zc, 0.005, rise)) for y, hw, zc, rise in SCREEN], cap=True)
        g.part('body')
        seam(g, fr, LOWER_SEAM)                                                                                   # the joint between the upper fairing and the lower one, and the one of the nose cone
        ring_seam(g, fr, NOSE_SEAM_Y)
        fasteners(g, fr, [(y, z + 0.020) for y, z in LOWER_SEAM[1:]])
        g.box('black', V(0, 0.335, 1.030), (0.14, 0.075, 0.034), r=0.012)                                        # the dash
        blob(g, 'black', V(0, 0.866, 0.737), (0.036, 0.024, 0.017), segs=18)                                     # the air intake in the nose
        for sd in (-1, 1):
            g.box('black', V(sd * 0.104, 0.812, 0.752), (0.136, 0.060, 0.066), r=0.014, seg=3, rot=(8, 0, sd * -26))   # the headlamps: a housing and a lens, angular
            g.box('lens', V(sd * 0.112, 0.838, 0.757), (0.118, 0.020, 0.048), r=0.010, seg=3, rot=(8, 0, sd * -26))
            c, rot = flank(fr, 0.340, 0.640, sd)
            g.box('black', c, (0.012, 0.150, 0.046), r=0.004, rot=rot)                                            # the side vent, on the flank
            for k in range(4):                                                                                     # the gills on the flank, by the knee
                c, rot = flank(fr, 0.150 - 0.034 * k, 0.700 - 0.012 * k, sd)
                g.box('black', c, (0.008, 0.050, 0.010), r=0.002, rot=(-30, rot[1], rot[2]))
            g.box('grip', V(sd * 0.1505, -0.115, 0.930), (0.012, 0.230, 0.085), r=0.004, rot=(0, 0, sd * -8))        # the knee pads of the tank
            g.rod('black', V(sd * 0.130, 0.40, 0.990), V(sd * 0.296, 0.352, 1.064), 0.008, segs=8)                 # the mirrors
            blob(g, 'paint2', V(sd * 0.318, 0.340, 1.076), (0.046, 0.024, 0.034), rot=(0, 0, sd * 14), segs=16)
            blob(g, 'glass', V(sd * 0.318, 0.327, 1.076), (0.040, 0.012, 0.028), rot=(0, 0, sd * 14), segs=14)
        g.loft('paint', rr_rings(scaled(TANK)), cap=True)
    else:
        g.loft('paint', rr_rings(scaled(TANK_NAKED)), cap=True)
        g.part('screen', pivot=(0, 0, 0))
        g.loft('smoke', [(y, arch(hw, zc, 0.005, rise)) for y, hw, zc, rise in ((0.560, 0.110, 1.100, 0.010), (0.500, 0.115, 1.150, 0.014), (0.450, 0.095, 1.190, 0.012))], cap=True)   # a flyscreen
        g.part('body')
        for sd in (-1, 1):
            g.rod('black', V(sd * 0.150, 0.40, 1.010), V(sd * 0.290, 0.350, 1.080), 0.008, segs=8)
            blob(g, 'paint2', V(sd * 0.312, 0.346, 1.090), (0.046, 0.024, 0.034), rot=(0, 0, sd * 14), segs=16)
            blob(g, 'glass', V(sd * 0.312, 0.333, 1.090), (0.040, 0.012, 0.028), rot=(0, 0, sd * 14), segs=14)
    g.loft('seat', rr_rings(SEAT), cap=True)
    g.loft('paint', hex_rings(TAIL), cap=True)
    g.box('black', V(0, -0.972, 0.944), (0.150, 0.032, 0.040), r=0.006)                                           # the tail light and its housing
    g.box('taillight', V(0, -0.989, 0.946), (0.126, 0.012, 0.026), r=0.004)
    g.box('black', V(0, -0.84, 0.78), (0.10, 0.20, 0.014), r=0.004, rot=(-8, 0, 0))                              # the hugger under the tail


def plates_preview(g, style):
    """Where the game puts the number plates (PistaBikeMesh.cs), as white boards: for the photographs only, they are not exported."""
    g.part('plates_preview', pivot=(0, 0, 0))
    front = (V(0, 0.80, 0.80) if style < 2 else V(0, 0.68, 0.99))
    g.box('plate', front, (0.200, 0.012, 0.125), r=0.002, rot=(30, 0, 0))
    for sd in (-1, 1):
        g.box('plate', V(sd * 0.1135, -0.64, 0.89), (0.008, 0.16, 0.094), r=0.001, rot=(0, 0, sd * -5))
    g.box('plate', V(0, -0.99, 0.865), (0.19, 0.008, 0.119), r=0.001)


# ----------------------------------------------------------------------------- the whole bike
def bike_materials():
    """The materials of the photographs: one per role (the game makes its own from the names)."""
    M = {}

    def mk(role, color, rough, metal=0.0, coat=0.0, emit=None, alpha=1.0):
        M[role] = preview_material(role, color, rough, metal, coat, emit, alpha)
    mk('paint', (0.60, 0.035, 0.040), 0.28, 0.0, 1.0)
    mk('paint2', (0.90, 0.90, 0.88), 0.30, 0.0, 1.0)
    mk('frame', (0.07, 0.07, 0.075), 0.45, 0.8)
    mk('metal', (0.62, 0.63, 0.66), 0.32, 1.0)
    mk('steel', (0.55, 0.55, 0.57), 0.30, 1.0)
    mk('rim', (0.62, 0.63, 0.66), 0.32, 1.0)
    mk('discF', (0.55, 0.55, 0.57), 0.30, 1.0)
    mk('discR', (0.55, 0.55, 0.57), 0.30, 1.0)
    mk('titan', (0.58, 0.55, 0.50), 0.28, 1.0)
    mk('engine', (0.16, 0.16, 0.17), 0.45, 0.9)
    mk('black', (0.012, 0.012, 0.014), 0.45)
    mk('carbon', (0.02, 0.02, 0.022), 0.30, 0.3, 0.6)
    mk('seat', (0.025, 0.025, 0.027), 0.55)
    mk('glass', (0.03, 0.04, 0.05), 0.06)
    mk('smoke', (0.02, 0.022, 0.026), 0.04, 0.0, 0.0, None, 0.42)                                                 # the windscreen: smoked, seen through
    mk('gold', (0.80, 0.58, 0.14), 0.30, 1.0)
    mk('caliper', (0.65, 0.05, 0.04), 0.30, 0.6)
    mk('grip', (0.015, 0.015, 0.016), 0.70)
    mk('chain', (0.30, 0.27, 0.15), 0.40, 1.0)
    mk('radiator', (0.02, 0.02, 0.025), 0.60, 0.5)
    mk('tyre', (0.016, 0.016, 0.018), 0.85)
    mk('lens', (0.90, 0.92, 0.95), 0.05, 0.0, 0.0, (0.9, 0.9, 0.8))
    mk('taillight', (0.70, 0.02, 0.02), 0.20, 0.0, 0.0, (1.0, 0.05, 0.03))
    mk('plate', (0.92, 0.92, 0.90), 0.5)
    return M


def build_bike(style, preview=False):
    """The bike of this style in the scene: (the Gun, its root). 'preview' adds the boards where the plates go."""
    mats = bike_materials()
    g = K.Gun('moto%d' % style)
    for role in ('paint', 'paint2', 'glass', 'smoke', 'seat', 'tyre', 'black'):
        g.bevel[role] = 0.0
    fork(g, style)
    swing(g)
    g.part('front', pivot=AXLE_F)
    wheel(g, AXLE_F, False)
    g.part('rear', pivot=AXLE_R)
    wheel(g, AXLE_R, True)
    frame(g, style)
    engine(g, style)
    exhaust(g, style)
    bodywork(g, style)
    if preview:
        plates_preview(g, style)
    root = g.build(bevel=0.0007, smooth_deg=40, material_fn=lambda role, gun: mats[role])
    return g, root
