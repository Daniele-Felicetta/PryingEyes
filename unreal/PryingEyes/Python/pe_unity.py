"""Reads the Unity project (assets, .meta, .mat) and builds the matching Unreal assets. Editor Python only.

Everything here is derived: the Unity folder stays the source of truth and Content/ is rebuilt from it.
Mirror rule: unity/PryingEyes/Assets/_PryingEyes/<dir>/<file> becomes /Game/Imported/<dir>/<file> (names sanitised).
Unity's URP Lit is translated to one master material (M_UnityLit) whose parameters carry what the .mat file says.
"""
import json
import os
import re

import unreal

REPO = os.path.abspath(os.path.join(unreal.Paths.project_dir(), "..", ".."))
UNITY_PROJECT = os.path.join(REPO, "unity", "PryingEyes")
UNITY_ASSETS = os.path.join(UNITY_PROJECT, "Assets")
UNITY_ROOT = os.path.join(UNITY_ASSETS, "_PryingEyes")
OUT_DIR = os.path.join(unreal.Paths.project_saved_dir(), "PE")

URP_LIT_GUIDS = {"933532a4fcc9baf4fa0491de14d08ed7", "8d2bb70cbf9db8d4da26e15b26e74248"}   # Lit, Simple Lit
URP_UNLIT_GUIDS = {"650dd9526735d5b46b79224bc6e94025"}

MASTER_PATH = "/Game/Imported/Materials"
MASTER_NAME = "M_UnityLit"

mel = unreal.MaterialEditingLibrary
atools = unreal.AssetToolsHelpers.get_asset_tools()
eal = unreal.EditorAssetLibrary


def log(*a):
    unreal.log("PE " + " ".join(str(x) for x in a))


def warn(*a):
    unreal.log_warning("PE " + " ".join(str(x) for x in a))


def sanitize(name):
    return re.sub(r"[^A-Za-z0-9_]", "_", name)


def _under(path, root):
    try:
        return os.path.commonpath([os.path.abspath(path), os.path.abspath(root)]) == os.path.abspath(root)
    except ValueError:
        return False


def ue_dir_for(unity_path):
    """Folder in /Game that mirrors the folder of a Unity file; files from elsewhere go to /Game/Imported/External."""
    folder = os.path.dirname(unity_path)
    if _under(folder, UNITY_ROOT):
        rel = os.path.relpath(folder, UNITY_ROOT).replace("\\", "/")
        prefix = ""
    elif _under(folder, UNITY_ASSETS):
        rel = os.path.relpath(folder, UNITY_ASSETS).replace("\\", "/")
        prefix = "Other/"
    else:
        return "/Game/Imported/External"
    parts = [sanitize(p) for p in rel.split("/") if p and p != "."]
    return "/Game/Imported/" + prefix + "/".join(parts) if parts else "/Game/Imported"


# ---------------------------------------------------------------------------------------------------------------------
# Unity metadata
# ---------------------------------------------------------------------------------------------------------------------
_guid = None


def guid_index():
    global _guid
    if _guid is None:
        _guid = {}
        rx = re.compile(r"^guid: ([0-9a-f]{32})", re.M)
        for root, _, files in os.walk(UNITY_ASSETS):
            for f in files:
                if not f.endswith(".meta"):
                    continue
                p = os.path.join(root, f)
                try:
                    with open(p, "r", encoding="utf-8", errors="ignore") as fh:
                        head = fh.read(240)
                except OSError:
                    continue
                m = rx.search(head)
                if m:
                    _guid[m.group(1)] = p[:-5]
        log("guid index", len(_guid), "assets")
    return _guid


def read_text(path):
    with open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def parse_mat(path):
    """Unity .mat (text YAML) -> {name, shader, tex: {prop: (guid, sx, sy, ox, oy)}, floats, colors, keywords}."""
    out = {"name": os.path.splitext(os.path.basename(path))[0], "shader": "", "tex": {}, "floats": {}, "colors": {},
           "keywords": []}
    section = None
    cur = None
    for line in read_text(path).splitlines():
        s = line.strip()
        m = re.match(r"m_Shader: \{fileID: \d+, guid: ([0-9a-f]{32})", s)
        if m:
            out["shader"] = m.group(1)
            continue
        if s.startswith("m_ShaderKeywords:"):
            out["keywords"] = s.split(":", 1)[1].split()
            continue
        if s in ("m_ValidKeywords:", "m_InvalidKeywords:"):
            section = "kw"
            continue
        if s == "m_TexEnvs:":
            section = "tex"
            continue
        if s in ("m_Ints:", "m_Ints: []"):
            section = "ints"
            continue
        if s in ("m_Floats:", "m_Floats: []"):
            section = "floats"
            continue
        if s in ("m_Colors:", "m_Colors: []"):
            section = "colors"
            continue
        if s.startswith("m_BuildTextureStacks") or s.startswith("m_Parent") or s.startswith("m_ModifiedSerialized"):
            section = None
        if section == "kw" and s.startswith("- "):
            out["keywords"].append(s[2:].strip())
        elif section == "tex":
            m = re.match(r"- (\w+):\s*$", s)
            if m:
                cur = m.group(1)
                out["tex"][cur] = ["", 1.0, 1.0, 0.0, 0.0]
                continue
            m = re.match(r"m_Texture: \{fileID: (\d+)(?:, guid: ([0-9a-f]{32}))?", s)
            if m and cur:
                out["tex"][cur][0] = m.group(2) or ""
                continue
            m = re.match(r"m_Scale: \{x: ([^,]+), y: ([^}]+)\}", s)
            if m and cur:
                out["tex"][cur][1], out["tex"][cur][2] = float(m.group(1)), float(m.group(2))
                continue
            m = re.match(r"m_Offset: \{x: ([^,]+), y: ([^}]+)\}", s)
            if m and cur:
                out["tex"][cur][3], out["tex"][cur][4] = float(m.group(1)), float(m.group(2))
        elif section == "floats":
            m = re.match(r"- (\w+): ([-+0-9.eE]+)$", s)
            if m:
                out["floats"][m.group(1)] = float(m.group(2))
        elif section == "colors":
            m = re.match(r"- (\w+): \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}", s)
            if m:
                out["colors"][m.group(1)] = [float(m.group(i)) for i in range(2, 6)]
    return out


def mat_from_file(mat_path):
    """A .mat file as the neutral dict translate_material takes."""
    raw = parse_mat(mat_path)
    idx = guid_index()
    tex = {}
    for prop, (guid, sx, sy, ox, oy) in raw["tex"].items():
        png = idx.get(guid) if guid else None
        if png and png.lower().endswith((".png", ".jpg", ".jpeg", ".tga")):
            tex[prop] = {"png": png, "sx": sx, "sy": sy, "ox": ox, "oy": oy}
    if raw["shader"] in URP_LIT_GUIDS:
        kind = "lit"
    elif raw["shader"] in URP_UNLIT_GUIDS:
        kind = "unlit"
    else:
        kind = "skip"
    return {"name": raw["name"], "kind": kind, "shader": raw["shader"], "keywords": set(raw["keywords"]),
            "floats": raw["floats"], "colors": raw["colors"], "tex": tex}


def mat_from_export(m, export_dir):
    """A material of the scene file written by Unity's UnrealExport.cs, as the same neutral dict."""
    shader = m.get("shader", "")
    low = shader.lower()
    if shader in ("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Complex Lit",
                  "Universal Render Pipeline/Baked Lit"):
        kind = "lit"
    elif "unlit" in low and shader.startswith("Universal Render Pipeline"):
        kind = "unlit"
    else:
        kind = "skip"
    tex = {}
    for t in m.get("tex", []):
        p = t["path"]
        png = os.path.join(UNITY_PROJECT, p) if p.startswith("Assets/") else os.path.join(export_dir, p)
        if os.path.exists(png):
            tex[t["prop"]] = {"png": png, "sx": t["sx"], "sy": t["sy"], "ox": t["ox"], "oy": t["oy"]}
    return {"name": m["name"], "kind": kind, "shader": shader, "keywords": set(m.get("keywords", [])),
            "floats": {f["name"]: f["v"] for f in m.get("floats", [])},
            "colors": {c["name"]: [c["r"], c["g"], c["b"], c["a"]] for c in m.get("colors", [])}, "tex": tex}


def texture_meta(png_path):
    """sRGB flag and kind of a texture as Unity imports it."""
    info = {"srgb": True, "normal": False}
    meta = png_path + ".meta"
    if os.path.exists(meta):
        t = read_text(meta)
        m = re.search(r"sRGBTexture: (\d)", t)
        if m:
            info["srgb"] = m.group(1) == "1"
        m = re.search(r"textureType: (\d+)", t)
        if m:
            info["normal"] = m.group(1) == "1"
    return info


def srgb_to_linear(c):
    """Unity converts colour properties from the inspector's gamma space to linear before the shader sees them."""
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


# ---------------------------------------------------------------------------------------------------------------------
# Importing
# ---------------------------------------------------------------------------------------------------------------------
def use_legacy_fbx():
    """The FBX options of the classic importer (FbxImportUI) are the documented way to script an import."""
    try:
        unreal.SystemLibrary.execute_console_command(None, "Interchange.FeatureFlags.Import.FBX 0")
    except Exception as e:  # noqa: BLE001
        warn("could not switch to the legacy FBX importer:", e)


def _task(filename, dest_dir, name, options=None):
    t = unreal.AssetImportTask()
    t.set_editor_property("automated", True)
    t.set_editor_property("filename", filename)
    t.set_editor_property("destination_path", dest_dir)
    t.set_editor_property("destination_name", name)
    t.set_editor_property("replace_existing", True)
    t.set_editor_property("replace_existing_settings", True)
    t.set_editor_property("save", False)
    if options is not None:
        t.set_editor_property("options", options)
    return t


_tex_done = {}


def import_texture(png_path, role):
    """role: 'color' | 'normal' | 'mask' (linear data, alpha kept). Returns the asset path."""
    key = (os.path.normcase(png_path), role)
    if key in _tex_done:
        return _tex_done[key]
    dest = ue_dir_for(png_path)
    name = sanitize(os.path.splitext(os.path.basename(png_path))[0])
    if role == "normal":
        name += "_n"      # the same file can serve two roles in different materials
    elif role == "mask":
        name += "_m"
    asset_path = dest + "/" + name
    info = texture_meta(png_path)
    if not eal.does_asset_exist(asset_path):
        atools.import_asset_tasks([_task(png_path, dest, name)])
    tex = unreal.load_asset(asset_path)
    if tex is None:
        warn("texture import failed:", png_path)
        return None
    if role == "normal":
        tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
        tex.set_editor_property("srgb", False)
        tex.set_editor_property("flip_green_channel", True)    # Unity normals are OpenGL style, Unreal's are DirectX style
        tex.set_editor_property("lod_group", unreal.TextureGroup.TEXTUREGROUP_WORLD_NORMAL_MAP)
    elif role == "mask":
        tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_BC7)
        tex.set_editor_property("srgb", False)
    else:
        tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_DEFAULT)
        tex.set_editor_property("srgb", info["srgb"])
    eal.save_loaded_asset(tex)
    _tex_done[key] = asset_path
    return asset_path


ENV_PATH = None    # the environment texture the next material instances will sample (see import_environment)


def import_environment(exr_path):
    """The scene's environment: Unity's custom reflection cube, drawn by the exporter as a 2:1 latitude-longitude HDR image,
    becomes a linear Texture2D (wrapping horizontally, with mips) that the master material samples. Sets ENV_PATH."""
    global ENV_PATH
    dest, name = "/Game/Imported/Env", "T_Env"
    if eal.does_asset_exist(dest + "/" + name):
        eal.delete_asset(dest + "/" + name)
    atools.import_asset_tasks([_task(exr_path, dest, name)])
    tex = unreal.load_asset(dest + "/" + name)
    if tex is None:
        warn("environment import failed:", exr_path)
        return None
    tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_HDR)
    tex.set_editor_property("srgb", False)
    tex.set_editor_property("address_x", unreal.TextureAddress.TA_WRAP)
    tex.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)
    tex.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_FROM_TEXTURE_GROUP)
    eal.save_loaded_asset(tex)
    ENV_PATH = dest + "/" + name
    log("environment", ENV_PATH, tex.blueprint_get_size_x(), "x", tex.blueprint_get_size_y())
    return ENV_PATH


FBX_SCALE = 100.0    # the models are authored in metres (1 unit = 1 m in Unity); Unreal's unit is the centimetre


def _convert_units(ui):
    """Scale every import-data object (mesh and animation alike, or the bone translations would not match)."""
    for prop in ("static_mesh_import_data", "skeletal_mesh_import_data", "anim_sequence_import_data"):
        try:
            data = ui.get_editor_property(prop)
            data.set_editor_property("import_uniform_scale", FBX_SCALE)
        except Exception as e:  # noqa: BLE001
            warn("import_uniform_scale on", prop, ":", e)


def import_fbx(fbx_path, skeletal, animation_skeleton=None, animations=False, name_suffix=""):
    """Imports a model (static or skeletal) or, with animation_skeleton, an animation. Returns the imported asset paths."""
    dest = ue_dir_for(fbx_path)
    name = sanitize(os.path.splitext(os.path.basename(fbx_path))[0]) + name_suffix
    ui = unreal.FbxImportUI()
    ui.set_editor_property("import_textures", False)
    ui.set_editor_property("import_materials", False)
    _convert_units(ui)
    if animation_skeleton is not None:
        ui.set_editor_property("import_mesh", False)
        ui.set_editor_property("import_animations", True)
        ui.set_editor_property("import_as_skeletal", True)
        ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_ANIMATION)
        ui.set_editor_property("skeleton", animation_skeleton)
    elif skeletal:
        ui.set_editor_property("import_mesh", True)
        ui.set_editor_property("import_animations", animations)
        ui.set_editor_property("import_as_skeletal", True)
        ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_SKELETAL_MESH)
        try:
            ui.skeletal_mesh_import_data.set_editor_property("import_morph_targets", False)
        except Exception as e:  # noqa: BLE001
            warn("skeletal import data:", e)
    else:
        ui.set_editor_property("import_mesh", True)
        ui.set_editor_property("import_animations", False)
        ui.set_editor_property("import_as_skeletal", False)
        ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_STATIC_MESH)
        try:
            ui.static_mesh_import_data.set_editor_property("combine_meshes", False)
            ui.static_mesh_import_data.set_editor_property("generate_lightmap_u_vs", False)
            ui.static_mesh_import_data.set_editor_property("auto_generate_collision", False)
        except Exception as e:  # noqa: BLE001
            warn("static import data:", e)
    task = _task(fbx_path, dest, name, ui)
    atools.import_asset_tasks([task])
    paths = [str(p) for p in task.get_editor_property("imported_object_paths")]
    log("fbx", os.path.basename(fbx_path), "->", paths)
    return paths


# ---------------------------------------------------------------------------------------------------------------------
# The master material: URP Lit's inputs as Unreal parameters
# ---------------------------------------------------------------------------------------------------------------------
def _write_png(path, w, h, rgba):
    import struct
    import zlib

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + bytes(rgba) * w for _ in range(h))
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
    with open(path, "wb") as fh:
        fh.write(png)


def white_linear():
    """A 4x4 white texture that is not sRGB: the default of the mask parameters (the engine's own white is sRGB and
    Unreal refuses a Linear Color sampler on it)."""
    asset_path = MASTER_PATH + "/T_WhiteLinear"
    if not eal.does_asset_exist(asset_path):
        os.makedirs(OUT_DIR, exist_ok=True)
        png = os.path.join(OUT_DIR, "T_WhiteLinear.png")
        _write_png(png, 4, 4, (255, 255, 255, 255))
        atools.import_asset_tasks([_task(png, MASTER_PATH, "T_WhiteLinear")])
        tex = unreal.load_asset(asset_path)
        tex.set_editor_property("srgb", False)
        tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_BC7)
        tex.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS)
        eal.save_loaded_asset(tex)
    return asset_path


def _mk(mat, cls, x, y, **props):
    n = mel.create_material_expression(mat, cls, x, y)
    for k, v in props.items():
        n.set_editor_property(k, v)
    return n


def _scalar(mat, name, value, x, y):
    return _mk(mat, unreal.MaterialExpressionScalarParameter, x, y, parameter_name=name, default_value=value)


def _vector(mat, name, rgba, x, y):
    return _mk(mat, unreal.MaterialExpressionVectorParameter, x, y, parameter_name=name,
               default_value=unreal.LinearColor(*rgba))


def _tex(mat, name, default_path, sampler, x, y, uv):
    node = _mk(mat, unreal.MaterialExpressionTextureSampleParameter2D, x, y, parameter_name=name,
               texture=unreal.load_asset(default_path), sampler_type=sampler)
    mel.connect_material_expressions(uv, "", node, "UVs")
    return node


def _link(a, ao, b, bi):
    if not mel.connect_material_expressions(a, ao, b, bi):
        warn("material link failed:", a.get_class().get_name(), ao or "<out>", "->", b.get_class().get_name(), bi or "<in>")


def _uv(mat, tile_x, tile_y, off_x, off_y, x, y):
    """TexCoord x tile + offset."""
    texcoord = _mk(mat, unreal.MaterialExpressionTextureCoordinate, x, y)
    tile = _mk(mat, unreal.MaterialExpressionAppendVector, x + 200, y - 60)
    _link(_scalar(mat, tile_x, 1.0, x, y + 120), "", tile, "A")
    _link(_scalar(mat, tile_y, 1.0, x, y + 200), "", tile, "B")
    mul = _mk(mat, unreal.MaterialExpressionMultiply, x + 400, y)
    _link(texcoord, "", mul, "A")
    _link(tile, "", mul, "B")
    if not off_x:
        return mul
    off = _mk(mat, unreal.MaterialExpressionAppendVector, x + 200, y + 120)
    _link(_scalar(mat, off_x, 0.0, x, y + 280), "", off, "A")
    _link(_scalar(mat, off_y, 0.0, x, y + 360), "", off, "B")
    add = _mk(mat, unreal.MaterialExpressionAdd, x + 550, y)
    _link(mul, "", add, "A")
    _link(off, "", add, "B")
    return add


def build_master():
    path = MASTER_PATH + "/" + MASTER_NAME
    if eal.does_asset_exist(path):
        mat = unreal.load_asset(path)
    else:
        mat = atools.create_asset(MASTER_NAME, MASTER_PATH, unreal.Material, unreal.MaterialFactoryNew())
    mel.delete_all_material_expressions(mat)
    # a game run cannot add usage flags by itself: without these the instances fall back to the default material
    for flag in ("used_with_skeletal_mesh", "used_with_morph_targets", "used_with_instanced_static_meshes",
                 "used_with_nanite"):
        try:
            mat.set_editor_property(flag, True)
        except Exception as e:  # noqa: BLE001
            warn("material flag", flag, ":", e)
    S = unreal.MaterialSamplerType
    white = "/Engine/EngineResources/WhiteSquareTexture"
    white_lin = white_linear()
    flat = "/Engine/EngineMaterials/DefaultNormal"

    uv = _uv(mat, "TileX", "TileY", "OffX", "OffY", -1500, 0)             # one tiling for the maps of the main set
    uv_detail = _uv(mat, "DetailTileX", "DetailTileY", None, None, -1500, 600)

    # colour: base map x tint x detail (Unity's _DETAIL_MULX2: albedo *= lerp(1, 2 x detail, scale))
    base = _tex(mat, "BaseMap", white, S.SAMPLERTYPE_COLOR, -700, -400, uv)
    tint = _vector(mat, "BaseColor", (1, 1, 1, 1), -700, -200)
    col = _mk(mat, unreal.MaterialExpressionMultiply, -450, -350)
    _link(base, "RGB", col, "A")
    _link(tint, "", col, "B")
    detail = _tex(mat, "DetailMap", white, S.SAMPLERTYPE_COLOR, -700, 640, uv_detail)
    two = _mk(mat, unreal.MaterialExpressionConstant, -700, 780, r=2.0)
    detail2 = _mk(mat, unreal.MaterialExpressionMultiply, -520, 700)
    _link(detail, "RGB", detail2, "A")
    _link(two, "", detail2, "B")
    one3 = _mk(mat, unreal.MaterialExpressionConstant3Vector, -520, 820, constant=unreal.LinearColor(1, 1, 1, 0))
    use_detail = _scalar(mat, "UseDetail", 0.0, -700, 860)
    detail_scale = _scalar(mat, "DetailScale", 1.0, -700, 940)
    detail_alpha = _mk(mat, unreal.MaterialExpressionMultiply, -520, 900)
    _link(use_detail, "", detail_alpha, "A")
    _link(detail_scale, "", detail_alpha, "B")
    detail_factor = _mk(mat, unreal.MaterialExpressionLinearInterpolate, -330, 760)
    _link(one3, "", detail_factor, "A")
    _link(detail2, "", detail_factor, "B")
    _link(detail_alpha, "", detail_factor, "Alpha")
    col2 = _mk(mat, unreal.MaterialExpressionMultiply, -200, -300)
    _link(col, "", col2, "A")
    _link(detail_factor, "", col2, "B")
    mel.connect_material_property(col2, "", unreal.MaterialProperty.MP_BASE_COLOR)

    # opacity (used only when an instance switches the blend mode to masked or translucent)
    alpha = _mk(mat, unreal.MaterialExpressionMultiply, -450, -120)
    _link(base, "A", alpha, "A")
    _link(tint, "A", alpha, "B")
    mel.connect_material_property(alpha, "", unreal.MaterialProperty.MP_OPACITY_MASK)
    mel.connect_material_property(alpha, "", unreal.MaterialProperty.MP_OPACITY)

    mask = _tex(mat, "MaskMap", white_lin, S.SAMPLERTYPE_LINEAR_COLOR, -700, 0, uv)
    use_mask = _scalar(mat, "UseMask", 0.0, -700, 200)

    metal_c = _scalar(mat, "Metallic", 0.0, -700, 280)
    metal = _mk(mat, unreal.MaterialExpressionLinearInterpolate, -450, 100)
    _link(metal_c, "", metal, "A")
    _link(mask, "R", metal, "B")
    _link(use_mask, "", metal, "Alpha")
    mel.connect_material_property(metal, "", unreal.MaterialProperty.MP_METALLIC)

    smooth_c = _scalar(mat, "Smoothness", 0.5, -700, 360)
    one = _mk(mat, unreal.MaterialExpressionConstant, -700, 440, r=1.0)
    sm_pick = _mk(mat, unreal.MaterialExpressionLinearInterpolate, -450, 260)
    _link(one, "", sm_pick, "A")
    _link(mask, "A", sm_pick, "B")
    _link(use_mask, "", sm_pick, "Alpha")
    sm = _mk(mat, unreal.MaterialExpressionMultiply, -250, 300)
    _link(sm_pick, "", sm, "A")
    _link(smooth_c, "", sm, "B")
    rough = _mk(mat, unreal.MaterialExpressionOneMinus, -50, 300)
    _link(sm, "", rough, "")
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)

    occ = _tex(mat, "OcclusionMap", white_lin, S.SAMPLERTYPE_LINEAR_COLOR, -700, 520, uv)
    occ_s = _scalar(mat, "OcclusionStrength", 0.0, -700, 700)
    ao = _mk(mat, unreal.MaterialExpressionLinearInterpolate, -450, 560)
    _link(one, "", ao, "A")
    _link(occ, "G", ao, "B")
    _link(occ_s, "", ao, "Alpha")
    mel.connect_material_property(ao, "", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)

    # normal: bump scale, then the detail normal added on top (scaled), the way URP blends them
    nrm = _tex(mat, "NormalMap", flat, S.SAMPLERTYPE_NORMAL, -700, 1100, uv)
    bump = _scalar(mat, "BumpScale", 1.0, -700, 1300)
    up = _mk(mat, unreal.MaterialExpressionConstant3Vector, -700, 1380, constant=unreal.LinearColor(0, 0, 1, 0))
    nlerp = _mk(mat, unreal.MaterialExpressionLinearInterpolate, -450, 1200)
    _link(up, "", nlerp, "A")
    _link(nrm, "RGB", nlerp, "B")
    _link(bump, "", nlerp, "Alpha")
    dn = _tex(mat, "DetailNormalMap", flat, S.SAMPLERTYPE_NORMAL, -700, 1500, uv_detail)
    dsub = _mk(mat, unreal.MaterialExpressionSubtract, -450, 1500)
    _link(dn, "RGB", dsub, "A")
    _link(up, "", dsub, "B")
    dscale = _scalar(mat, "DetailNormalScale", 0.0, -450, 1650)
    dmul = _mk(mat, unreal.MaterialExpressionMultiply, -250, 1560)
    _link(dsub, "", dmul, "A")
    _link(dscale, "", dmul, "B")
    nfinal = _mk(mat, unreal.MaterialExpressionAdd, -50, 1300)
    _link(nlerp, "", nfinal, "A")
    _link(dmul, "", nfinal, "B")
    mel.connect_material_property(nfinal, "", unreal.MaterialProperty.MP_NORMAL)

    # The emissive input carries two terms that Unity applies straight to the picture: the material's emission and the scene's
    # flat trilight ambient (sky above, ground below, equator between, by the pixel normal; it reaches everything, in shadow
    # or not, and is multiplied by the albedo and the occlusion). Unity's colours are picture-referred (1.0 = white after the
    # tone mapper), Unreal's radiance goes through the camera exposure first: both terms are multiplied by RadianceScale,
    # which the game sets from its light K. The defaults (scale 1, no ambient) leave a plain emissive material.
    emi_map = _tex(mat, "EmissionMap", white, S.SAMPLERTYPE_COLOR, -700, 1800, uv)
    emi_col = _vector(mat, "EmissionColor", (0, 0, 0, 1), -700, 2000)
    emi = _mk(mat, unreal.MaterialExpressionMultiply, -450, 1900)
    _link(emi_map, "RGB", emi, "A")
    _link(emi_col, "", emi, "B")
    rad = _scalar(mat, "RadianceScale", 1.0, -700, 2150)
    emi_scaled = _mk(mat, unreal.MaterialExpressionMultiply, -250, 1950)
    _link(emi, "", emi_scaled, "A")
    _link(rad, "", emi_scaled, "B")

    n_ws = _mk(mat, unreal.MaterialExpressionPixelNormalWS, -900, 2300)
    nz = _mk(mat, unreal.MaterialExpressionComponentMask, -700, 2300, r=False, g=False, b=True, a=False)
    _link(n_ws, "", nz, "")
    up = _mk(mat, unreal.MaterialExpressionSaturate, -520, 2260)
    _link(nz, "", up, "")
    minus_one = _mk(mat, unreal.MaterialExpressionConstant, -700, 2420, r=-1.0)
    nz_neg = _mk(mat, unreal.MaterialExpressionMultiply, -520, 2400)
    _link(nz, "", nz_neg, "A")
    _link(minus_one, "", nz_neg, "B")
    down = _mk(mat, unreal.MaterialExpressionSaturate, -340, 2400)
    _link(nz_neg, "", down, "")
    amb_sky = _vector(mat, "AmbSky", (0, 0, 0, 1), -900, 2560)
    amb_eq = _vector(mat, "AmbEquator", (0, 0, 0, 1), -900, 2660)
    amb_gnd = _vector(mat, "AmbGround", (0, 0, 0, 1), -900, 2760)
    d_sky = _mk(mat, unreal.MaterialExpressionSubtract, -700, 2560)
    _link(amb_sky, "", d_sky, "A")
    _link(amb_eq, "", d_sky, "B")
    d_gnd = _mk(mat, unreal.MaterialExpressionSubtract, -700, 2700)
    _link(amb_gnd, "", d_gnd, "A")
    _link(amb_eq, "", d_gnd, "B")
    t_sky = _mk(mat, unreal.MaterialExpressionMultiply, -520, 2560)
    _link(d_sky, "", t_sky, "A")
    _link(up, "", t_sky, "B")
    t_gnd = _mk(mat, unreal.MaterialExpressionMultiply, -520, 2700)
    _link(d_gnd, "", t_gnd, "A")
    _link(down, "", t_gnd, "B")
    amb_a = _mk(mat, unreal.MaterialExpressionAdd, -340, 2600)
    _link(amb_eq, "", amb_a, "A")
    _link(t_sky, "", amb_a, "B")
    amb = _mk(mat, unreal.MaterialExpressionAdd, -160, 2650)
    _link(amb_a, "", amb, "A")
    _link(t_gnd, "", amb, "B")
    diffuse_share = _mk(mat, unreal.MaterialExpressionOneMinus, -450, 2900)       # a metal has no diffuse to light
    _link(metal, "", diffuse_share, "")
    a1 = _mk(mat, unreal.MaterialExpressionMultiply, -250, 2900)
    _link(col2, "", a1, "A")
    _link(diffuse_share, "", a1, "B")
    a2 = _mk(mat, unreal.MaterialExpressionMultiply, -50, 2900)
    _link(a1, "", a2, "A")
    _link(ao, "", a2, "B")
    a3 = _mk(mat, unreal.MaterialExpressionMultiply, 150, 2800)
    _link(a2, "", a3, "A")
    _link(amb, "", a3, "B")
    a4 = _mk(mat, unreal.MaterialExpressionMultiply, 350, 2800)
    _link(a3, "", a4, "A")
    _link(rad, "", a4, "B")
    total = _mk(mat, unreal.MaterialExpressionAdd, 550, 2300)
    _link(emi_scaled, "", total, "A")
    _link(a4, "", total, "B")

    # Environment reflection, URP's way (GlossyEnvironmentReflection x EnvironmentBRDFSpecular) with the scene's custom
    # reflection cube as the environment. The exporter draws that cube as a 2:1 latitude-longitude image (EnvMap); the lookup
    # direction is turned from Unreal's axes back into Unity's, and the mip level comes from the roughness, as in URP.
    env_map = unreal.load_asset(white_lin)
    env_use = _scalar(mat, "UseEnv", 1.0, 700, 3300)             # 0 for an unlit surface: Unity's Unlit shader has no reflections
    env_k = _scalar(mat, "EnvIntensity", 0.0, 700, 3400)         # Unity's reflection intensity; 0 where the scene has no environment

    cam = _mk(mat, unreal.MaterialExpressionCameraVectorWS, 700, 2800)           # surface -> camera
    dot_nv = _mk(mat, unreal.MaterialExpressionDotProduct, 900, 2800)
    _link(n_ws, "", dot_nv, "A")
    _link(cam, "", dot_nv, "B")
    two = _mk(mat, unreal.MaterialExpressionConstant, 900, 2900, r=2.0)
    dot2 = _mk(mat, unreal.MaterialExpressionMultiply, 1100, 2850)
    _link(dot_nv, "", dot2, "A")
    _link(two, "", dot2, "B")
    n_scaled = _mk(mat, unreal.MaterialExpressionMultiply, 1300, 2850)
    _link(n_ws, "", n_scaled, "A")
    _link(dot2, "", n_scaled, "B")
    refl = _mk(mat, unreal.MaterialExpressionSubtract, 1500, 2850)               # reflect(-V, N) = 2 (N.V) N - V
    _link(n_scaled, "", refl, "A")
    _link(cam, "", refl, "B")

    def channel(src, r, g, b, y):
        m = _mk(mat, unreal.MaterialExpressionComponentMask, 1700, y, r=r, g=g, b=b, a=False)
        _link(src, "", m, "")
        return m

    rx = channel(refl, True, False, False, 2750)
    ry = channel(refl, False, True, False, 2850)
    rz = channel(refl, False, False, True, 2950)
    lon = _mk(mat, unreal.MaterialExpressionArctangent2, 1900, 2800)             # Unity's x is Unreal's Y, Unity's z is Unreal's X
    _link(ry, "", lon, "Y")
    _link(rx, "", lon, "X")
    inv_2pi = _mk(mat, unreal.MaterialExpressionConstant, 1900, 2900, r=0.15915494)
    half = _mk(mat, unreal.MaterialExpressionConstant, 1900, 3000, r=0.5)
    inv_pi = _mk(mat, unreal.MaterialExpressionConstant, 1900, 3100, r=0.31830988)
    u_a = _mk(mat, unreal.MaterialExpressionMultiply, 2100, 2800)
    _link(lon, "", u_a, "A")
    _link(inv_2pi, "", u_a, "B")
    u = _mk(mat, unreal.MaterialExpressionAdd, 2300, 2800)
    _link(u_a, "", u, "A")
    _link(half, "", u, "B")
    rz_c = _mk(mat, unreal.MaterialExpressionClamp, 1900, 2950, min_default=-1.0, max_default=1.0)
    _link(rz, "", rz_c, "")
    lat = _mk(mat, unreal.MaterialExpressionArcsine, 2100, 2950)
    _link(rz_c, "", lat, "")
    v_a = _mk(mat, unreal.MaterialExpressionMultiply, 2300, 2950)
    _link(lat, "", v_a, "A")
    _link(inv_pi, "", v_a, "B")
    v = _mk(mat, unreal.MaterialExpressionSubtract, 2500, 2900)
    _link(half, "", v, "A")
    _link(v_a, "", v, "B")
    env_uv = _mk(mat, unreal.MaterialExpressionAppendVector, 2700, 2850)
    _link(u, "", env_uv, "A")
    _link(v, "", env_uv, "B")

    # mip = perceptualRoughness (1.7 - 0.7 perceptualRoughness) x 9.9. URP says 6 for its pre-convolved cube; the lat-long image has
    # only box-filtered mips, whose texels are far narrower than a rough lobe, so the scale is widened until roughness 1 reaches
    # the 1 x 1 mip (the image's mean), which is where a fully rough URP lookup ends up as well
    m_a = _mk(mat, unreal.MaterialExpressionMultiply, 1100, 3150)
    c07 = _mk(mat, unreal.MaterialExpressionConstant, 900, 3150, r=-0.7)
    _link(rough, "", m_a, "A")
    _link(c07, "", m_a, "B")
    m_b = _mk(mat, unreal.MaterialExpressionAdd, 1300, 3150)
    c17 = _mk(mat, unreal.MaterialExpressionConstant, 1100, 3250, r=1.7)
    _link(m_a, "", m_b, "A")
    _link(c17, "", m_b, "B")
    m_c = _mk(mat, unreal.MaterialExpressionMultiply, 1500, 3150)
    _link(rough, "", m_c, "A")
    _link(m_b, "", m_c, "B")
    c6 = _mk(mat, unreal.MaterialExpressionConstant, 1500, 3250, r=9.9)
    mip = _mk(mat, unreal.MaterialExpressionMultiply, 1700, 3150)
    _link(m_c, "", mip, "A")
    _link(c6, "", mip, "B")

    env_sample = _mk(mat, unreal.MaterialExpressionTextureSampleParameter2D, 2900, 2900, parameter_name="EnvMap",
                     texture=env_map, sampler_type=S.SAMPLERTYPE_LINEAR_COLOR,
                     mip_value_mode=unreal.TextureMipValueMode.TMVM_MIP_LEVEL)
    _link(env_uv, "", env_sample, "UVs")
    _link(mip, "", env_sample, "Level")           # the editor shortens the pin "MipLevel" to "Level" (UMaterialGraphNode::GetShortenPinName)

    # fresnel = (1 - saturate(N.V))^4
    ndv_s = _mk(mat, unreal.MaterialExpressionSaturate, 1100, 3350)
    _link(dot_nv, "", ndv_s, "")
    one_m = _mk(mat, unreal.MaterialExpressionOneMinus, 1300, 3350)
    _link(ndv_s, "", one_m, "")
    f2 = _mk(mat, unreal.MaterialExpressionMultiply, 1500, 3350)
    _link(one_m, "", f2, "A")
    _link(one_m, "", f2, "B")
    fres = _mk(mat, unreal.MaterialExpressionMultiply, 1700, 3350)
    _link(f2, "", fres, "A")
    _link(f2, "", fres, "B")
    # F0 = lerp(0.04, albedo, metallic); grazing = saturate(smoothness + reflectivity), reflectivity = lerp(0.04, 1, metallic)
    dielectric = _mk(mat, unreal.MaterialExpressionConstant3Vector, 1500, 3500, constant=unreal.LinearColor(0.04, 0.04, 0.04, 0))
    f0 = _mk(mat, unreal.MaterialExpressionLinearInterpolate, 1700, 3500)
    _link(dielectric, "", f0, "A")
    _link(col2, "", f0, "B")
    _link(metal, "", f0, "Alpha")
    c004 = _mk(mat, unreal.MaterialExpressionConstant, 1500, 3650, r=0.04)
    c1 = _mk(mat, unreal.MaterialExpressionConstant, 1500, 3750, r=1.0)
    reflectivity = _mk(mat, unreal.MaterialExpressionLinearInterpolate, 1700, 3700)
    _link(c004, "", reflectivity, "A")
    _link(c1, "", reflectivity, "B")
    _link(metal, "", reflectivity, "Alpha")
    smooth_plus = _mk(mat, unreal.MaterialExpressionAdd, 1900, 3650)
    _link(sm, "", smooth_plus, "A")
    _link(reflectivity, "", smooth_plus, "B")
    grazing = _mk(mat, unreal.MaterialExpressionSaturate, 2100, 3650)
    _link(smooth_plus, "", grazing, "")
    spec_a = _mk(mat, unreal.MaterialExpressionLinearInterpolate, 2300, 3550)
    _link(f0, "", spec_a, "A")
    _link(grazing, "", spec_a, "B")
    _link(fres, "", spec_a, "Alpha")
    # surfaceReduction = 1 / (perceptualRoughness^4 + 1)
    p2 = _mk(mat, unreal.MaterialExpressionMultiply, 1900, 3850)
    _link(rough, "", p2, "A")
    _link(rough, "", p2, "B")
    p4 = _mk(mat, unreal.MaterialExpressionMultiply, 2100, 3850)
    _link(p2, "", p4, "A")
    _link(p2, "", p4, "B")
    p4_1 = _mk(mat, unreal.MaterialExpressionAdd, 2300, 3850)
    c1b = _mk(mat, unreal.MaterialExpressionConstant, 2100, 3950, r=1.0)
    _link(p4, "", p4_1, "A")
    _link(c1b, "", p4_1, "B")
    reduction = _mk(mat, unreal.MaterialExpressionDivide, 2500, 3850)
    _link(c1b, "", reduction, "A")
    _link(p4_1, "", reduction, "B")
    spec_term = _mk(mat, unreal.MaterialExpressionMultiply, 2700, 3700)
    _link(spec_a, "", spec_term, "A")
    _link(reduction, "", spec_term, "B")
    # colour from the cube x specular term x occlusion x (intensity, unlit switch, radiance scale)
    e1 = _mk(mat, unreal.MaterialExpressionMultiply, 3100, 3300)
    _link(env_sample, "RGB", e1, "A")
    _link(spec_term, "", e1, "B")
    e2 = _mk(mat, unreal.MaterialExpressionMultiply, 3300, 3300)
    _link(e1, "", e2, "A")
    _link(ao, "", e2, "B")
    gain_a = _mk(mat, unreal.MaterialExpressionMultiply, 1100, 3450)
    _link(env_k, "", gain_a, "A")
    _link(env_use, "", gain_a, "B")
    gain = _mk(mat, unreal.MaterialExpressionMultiply, 1300, 3450)
    _link(gain_a, "", gain, "A")
    _link(rad, "", gain, "B")
    e3 = _mk(mat, unreal.MaterialExpressionMultiply, 3500, 3300)
    _link(e2, "", e3, "A")
    _link(gain, "", e3, "B")
    emissive = _mk(mat, unreal.MaterialExpressionAdd, 3700, 2800)
    _link(total, "", emissive, "A")
    _link(e3, "", emissive, "B")

    # DebugMode (0 = off): shows one intermediate of the environment term instead of the picture, for finding wiring mistakes
    dbg = _scalar(mat, "DebugMode", 0.0, 3900, 3400)
    shown = emissive
    # a fixed look-up (the middle of the ceiling, mip 6) and one at the surface's own texture coordinates, mip 0
    fixed_uv = _mk(mat, unreal.MaterialExpressionConstant2Vector, 2700, 4300, r=0.5, g=0.5)
    fixed_mip = _mk(mat, unreal.MaterialExpressionConstant, 2700, 4400, r=10.0)       # the 1 x 1 top mip: the image's mean
    fixed = _mk(mat, unreal.MaterialExpressionTextureSampleParameter2D, 2900, 4300, parameter_name="EnvMap",
                texture=env_map, sampler_type=S.SAMPLERTYPE_LINEAR_COLOR, mip_value_mode=unreal.TextureMipValueMode.TMVM_MIP_LEVEL)
    _link(fixed_uv, "", fixed, "UVs")
    _link(fixed_mip, "", fixed, "Level")
    at_uv = _mk(mat, unreal.MaterialExpressionTextureSampleParameter2D, 2900, 4600, parameter_name="EnvMap",
                texture=env_map, sampler_type=S.SAMPLERTYPE_LINEAR_COLOR)
    _link(_mk(mat, unreal.MaterialExpressionTextureCoordinate, 2700, 4600), "", at_uv, "UVs")
    for n, (label, src, scale) in enumerate((("lookup uv", env_uv, 1.0), ("reflection vector", refl, 0.5), ("raw cube colour", env_sample, 0.1),
                                              ("specular term", spec_term, 10.0), ("pixel normal", n_ws, 0.5), ("fresnel", fres, 1.0),
                                              ("raw cube colour", fixed, 0.1), ("raw cube colour", at_uv, 0.1)), start=1):
        x = 4100 + n * 200
        if label == "raw cube colour":
            picked = _mk(mat, unreal.MaterialExpressionComponentMask, x, 3000, r=True, g=True, b=True, a=False)
            _link(src, "RGB", picked, "")
            src = picked
        elif label in ("lookup uv",):
            pad = _mk(mat, unreal.MaterialExpressionAppendVector, x, 3000)
            _link(src, "", pad, "A")
            _link(_mk(mat, unreal.MaterialExpressionConstant, x, 3100, r=0.0), "", pad, "B")
            src = pad
        sc = _mk(mat, unreal.MaterialExpressionMultiply, x, 3200)
        _link(src, "", sc, "A")
        _link(_mk(mat, unreal.MaterialExpressionConstant, x, 3300, r=scale), "", sc, "B")
        if label in ("reflection vector", "pixel normal"):
            bias = _mk(mat, unreal.MaterialExpressionAdd, x, 3400)
            _link(sc, "", bias, "A")
            _link(_mk(mat, unreal.MaterialExpressionConstant, x, 3500, r=0.5), "", bias, "B")
            sc = bias
        sr = _mk(mat, unreal.MaterialExpressionMultiply, x, 3550)            # through the radiance scale, to be visible
        _link(sc, "", sr, "A")
        _link(rad, "", sr, "B")
        sc = sr
        off = _mk(mat, unreal.MaterialExpressionSubtract, x, 3600)
        _link(dbg, "", off, "A")
        _link(_mk(mat, unreal.MaterialExpressionConstant, x, 3700, r=float(n)), "", off, "B")
        ab = _mk(mat, unreal.MaterialExpressionAbs, x, 3800)
        _link(off, "", ab, "")
        inv = _mk(mat, unreal.MaterialExpressionOneMinus, x, 3900)
        _link(ab, "", inv, "")
        wgt = _mk(mat, unreal.MaterialExpressionSaturate, x, 4000)
        _link(inv, "", wgt, "")
        mix = _mk(mat, unreal.MaterialExpressionLinearInterpolate, x, 4100)
        _link(shown, "", mix, "A")
        _link(sc, "", mix, "B")
        _link(wgt, "", mix, "Alpha")
        shown = mix
    mel.connect_material_property(shown, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)

    mel.recompile_material(mat)
    eal.save_loaded_asset(mat)
    log("master material", path)
    return mat


GRADE_NAME = "M_UnityGrade"

# URP's contrast and Split Toning, step for step (LutBuilderHdr.shader, ACES workflow): the colour in ACEScg; contrast, a power
# about mid grey (the log curve's slope makes it exactly that); to gamma 2.2; a soft-light blend with the shadow tint and then the
# highlight tint (each faded towards neutral by the pixel's luma plus the balance); back to linear.
# The post-process material sees the scene in engine units: Unity's value is that times ToUnity (post exposure / radiance scale).
GRADE_HLSL = """
float3 x = max(InColor.rgb * ToUnity, 0.0);
float3 a = mul(float3x3(0.613097, 0.339523, 0.047379, 0.070194, 0.916354, 0.013452, 0.020616, 0.109570, 0.869815), x);
a = 0.18 * pow(max(a, 0.0) / 0.18, Contrast);
float3 g = pow(max(a, 0.0), 1.0 / 2.2);
float luma = saturate(dot(saturate(g), float3(0.272229, 0.674082, 0.0536895)) + Balance);
float3 half3v = float3(0.5, 0.5, 0.5);
float3 b1 = lerp(half3v, SplitS.rgb, 1.0 - luma);
float3 r1 = 2.0 * g * b1 + g * g * (1.0 - 2.0 * b1);
float3 r2 = sqrt(g) * (2.0 * b1 - 1.0) + 2.0 * g * (1.0 - b1);
float3 t = step(0.5, b1);
g = r2 * t + (1.0 - t) * r1;
float3 b2 = lerp(half3v, SplitH.rgb, luma);
r1 = 2.0 * g * b2 + g * g * (1.0 - 2.0 * b2);
r2 = sqrt(g) * (2.0 * b2 - 1.0) + 2.0 * g * (1.0 - b2);
t = step(0.5, b2);
g = r2 * t + (1.0 - t) * r1;
a = pow(max(g, 0.0), 2.2);
x = mul(float3x3(1.705051, -0.621792, -0.083259, -0.130256, 1.140805, -0.010548, -0.024003, -0.128969, 1.152972), a);
return max(x, 0.0) / ToUnity;
"""


def build_grade_material():
    """The game's contrast and Split Toning as a post-process material (scene colour before bloom). Parameters: ToUnity, Contrast,
    Balance, SplitShadows, SplitHighlights. The game mode (APEGameMode::SpawnGrade) makes an instance from the look and adds it to
    the post-process volume."""
    path = MASTER_PATH + "/" + GRADE_NAME
    if eal.does_asset_exist(path):
        mat = unreal.load_asset(path)
    else:
        mat = atools.create_asset(GRADE_NAME, MASTER_PATH, unreal.Material, unreal.MaterialFactoryNew())
    mel.delete_all_material_expressions(mat)
    mat.set_editor_property("material_domain", unreal.MaterialDomain.MD_POST_PROCESS)
    mat.set_editor_property("blendable_location", unreal.BlendableLocation.BL_SCENE_COLOR_BEFORE_BLOOM)   # before the tone mapper
    scene = _mk(mat, unreal.MaterialExpressionSceneTexture, -700, 0,
                scene_texture_id=unreal.SceneTextureId.PPI_POST_PROCESS_INPUT0)
    custom = _mk(mat, unreal.MaterialExpressionCustom, 0, 0, code=GRADE_HLSL, description="UnitySplitToning",
                 output_type=unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    inputs = []
    for name in ("InColor", "ToUnity", "Contrast", "Balance", "SplitS", "SplitH"):
        ci = unreal.CustomInput()
        ci.set_editor_property("input_name", name)
        inputs.append(ci)
    custom.set_editor_property("inputs", inputs)
    _link(scene, "Color", custom, "InColor")
    _link(_scalar(mat, "ToUnity", 1.0, -700, 300), "", custom, "ToUnity")
    _link(_scalar(mat, "Contrast", 1.0, -700, 350), "", custom, "Contrast")
    _link(_scalar(mat, "Balance", 0.0, -700, 400), "", custom, "Balance")
    _link(_vector(mat, "SplitShadows", (0.5, 0.5, 0.5, 1), -700, 500), "", custom, "SplitS")
    _link(_vector(mat, "SplitHighlights", (0.5, 0.5, 0.5, 1), -700, 650), "", custom, "SplitH")
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    mel.recompile_material(mat)
    eal.save_loaded_asset(mat)
    log("grade material", path)
    return mat


_mic_done = {}


def translate_material(data, dest, name):
    """Neutral material dict -> material instance of the master (URP Lit's rules, keywords included). Returns its path,
    or None for a shader that has no equivalent here (the haze cards, dust and lens shaders of the game)."""
    if data["kind"] == "skip":
        return None
    master = unreal.load_asset(MASTER_PATH + "/" + MASTER_NAME)
    asset_path = dest + "/" + name
    if asset_path in _mic_done:
        return asset_path
    if eal.does_asset_exist(asset_path):
        eal.delete_asset(asset_path)
    mic = atools.create_asset(name, dest, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    mel.set_material_instance_parent(mic, master)
    kw, f, c, tex = data["keywords"], data["floats"], data["colors"], data["tex"]
    unlit = data["kind"] == "unlit"
    if ENV_PATH:
        env = unreal.load_asset(ENV_PATH)
        if env is not None:
            mel.set_material_instance_texture_parameter_value(mic, "EnvMap", env)
    if unlit:
        mel.set_material_instance_scalar_parameter_value(mic, "UseEnv", 0.0)

    def texture(prop, role):
        entry = tex.get(prop)
        if not entry:
            return None, None
        ap = import_texture(entry["png"], role)
        return (unreal.load_asset(ap) if ap else None), entry

    def lin(rgba):
        return unreal.LinearColor(srgb_to_linear(rgba[0]), srgb_to_linear(rgba[1]), srgb_to_linear(rgba[2]), 1.0)

    base, entry = texture("_BaseMap", "color")
    if base is None:
        base, entry = texture("_MainTex", "color")
    if base is not None:
        mel.set_material_instance_texture_parameter_value(mic, "BaseMap", base)
    if entry:
        mel.set_material_instance_scalar_parameter_value(mic, "TileX", entry["sx"])
        mel.set_material_instance_scalar_parameter_value(mic, "TileY", entry["sy"])
        mel.set_material_instance_scalar_parameter_value(mic, "OffX", entry["ox"])
        mel.set_material_instance_scalar_parameter_value(mic, "OffY", entry["oy"])
    tint = c.get("_BaseColor", c.get("_Color", [1, 1, 1, 1]))
    if unlit:
        # an unlit surface is all emission: no base colour, the texture times the tint goes to the emissive input
        mel.set_material_instance_vector_parameter_value(mic, "BaseColor", unreal.LinearColor(0, 0, 0, 1))
        if base is not None:
            mel.set_material_instance_texture_parameter_value(mic, "EmissionMap", base)
        e = lin(tint)
        mel.set_material_instance_vector_parameter_value(mic, "EmissionColor", e)
    else:
        l = lin(tint)
        l.a = tint[3]
        mel.set_material_instance_vector_parameter_value(mic, "BaseColor", l)
        if "_NORMALMAP" in kw:
            nrm, _ = texture("_BumpMap", "normal")
            if nrm is not None:
                mel.set_material_instance_texture_parameter_value(mic, "NormalMap", nrm)
                mel.set_material_instance_scalar_parameter_value(mic, "BumpScale", f.get("_BumpScale", 1.0))
        maskt = None
        if "_METALLICSPECGLOSSMAP" in kw:
            maskt, _ = texture("_MetallicGlossMap", "mask")
        if maskt is not None:
            mel.set_material_instance_texture_parameter_value(mic, "MaskMap", maskt)
            mel.set_material_instance_scalar_parameter_value(mic, "UseMask", 1.0)
        else:
            mel.set_material_instance_scalar_parameter_value(mic, "Metallic", f.get("_Metallic", 0.0))
        mel.set_material_instance_scalar_parameter_value(mic, "Smoothness", f.get("_Smoothness", 0.5))
        if "_OCCLUSIONMAP" in kw:
            occt, _ = texture("_OcclusionMap", "mask")
            if occt is not None:
                mel.set_material_instance_texture_parameter_value(mic, "OcclusionMap", occt)
                mel.set_material_instance_scalar_parameter_value(mic, "OcclusionStrength", f.get("_OcclusionStrength", 1.0))
        if "_DETAIL_MULX2" in kw or "_DETAIL_SCALED" in kw:
            dt, dentry = texture("_DetailAlbedoMap", "color")
            if dt is not None:
                mel.set_material_instance_texture_parameter_value(mic, "DetailMap", dt)
                mel.set_material_instance_scalar_parameter_value(mic, "DetailTileX", dentry["sx"])
                mel.set_material_instance_scalar_parameter_value(mic, "DetailTileY", dentry["sy"])
                mel.set_material_instance_scalar_parameter_value(mic, "UseDetail", 1.0)
                mel.set_material_instance_scalar_parameter_value(mic, "DetailScale", f.get("_DetailAlbedoMapScale", 1.0))
            dn, dnentry = texture("_DetailNormalMap", "normal")
            if dn is not None:
                mel.set_material_instance_texture_parameter_value(mic, "DetailNormalMap", dn)
                mel.set_material_instance_scalar_parameter_value(mic, "DetailNormalScale", f.get("_DetailNormalMapScale", 1.0))
                if dt is None:
                    mel.set_material_instance_scalar_parameter_value(mic, "DetailTileX", dnentry["sx"])
                    mel.set_material_instance_scalar_parameter_value(mic, "DetailTileY", dnentry["sy"])
        if "_EMISSION" in kw:
            emt, _ = texture("_EmissionMap", "color")
            if emt is not None:
                mel.set_material_instance_texture_parameter_value(mic, "EmissionMap", emt)
            mel.set_material_instance_vector_parameter_value(mic, "EmissionColor", lin(c.get("_EmissionColor", [0, 0, 0, 1])))

    # blend mode and sidedness: instance overrides
    try:
        ov = mic.get_editor_property("base_property_overrides")
        changed = False
        if f.get("_Surface", 0.0) >= 0.5:
            ov.set_editor_property("override_blend_mode", True)
            ov.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
            changed = True
        elif f.get("_AlphaClip", 0.0) >= 0.5 or "_ALPHATEST_ON" in kw:
            ov.set_editor_property("override_blend_mode", True)
            ov.set_editor_property("blend_mode", unreal.BlendMode.BLEND_MASKED)
            ov.set_editor_property("override_opacity_mask_clip_value", True)
            ov.set_editor_property("opacity_mask_clip_value", f.get("_Cutoff", 0.5))
            changed = True
        if f.get("_Cull", 2.0) == 0.0:
            ov.set_editor_property("override_two_sided", True)
            ov.set_editor_property("two_sided", True)
            changed = True
        if changed:
            mic.set_editor_property("base_property_overrides", ov)
    except Exception as e:  # noqa: BLE001
        warn("blend overrides of", name, ":", e)
    mel.update_material_instance(mic)
    eal.save_loaded_asset(mic)
    _mic_done[asset_path] = True
    return asset_path


def unity_material(mat_path):
    """One Unity .mat -> its material instance next to the mirrored folder. Returns the asset path (or None)."""
    data = mat_from_file(mat_path)
    return translate_material(data, ue_dir_for(mat_path), "MI_" + sanitize(data["name"]))


def fbx_material_map(fbx_path):
    """{FBX material name: .mat path} from the model's .meta (externalObjects), the way Unity binds them."""
    out = {}
    meta = fbx_path + ".meta"
    if not os.path.exists(meta):
        return out
    idx = guid_index()
    cur = None
    for line in read_text(meta).splitlines():
        s = line.strip()
        m = re.match(r"name: (.+)$", s)
        if m:
            cur = m.group(1).strip()
            continue
        m = re.match(r"second: \{fileID: 2100000, guid: ([0-9a-f]{32})", s)
        if m and cur:
            p = idx.get(m.group(1))
            if p and p.lower().endswith(".mat"):
                out[cur] = p
            cur = None
    return out


def write_json(name, obj):
    os.makedirs(OUT_DIR, exist_ok=True)
    p = os.path.join(OUT_DIR, name)
    with open(p, "w", encoding="utf-8") as fh:
        json.dump(obj, fh, indent=1)
    log("wrote", p)
    return p
