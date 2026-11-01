import bpy, math, sys, os

OUT = sys.argv[sys.argv.index("--") + 1]
os.makedirs(OUT, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def apply(obj, m):
    obj.data.materials.append(m)
    return obj


def sphere(loc, scale, m, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(location=loc, segments=seg, ring_count=seg // 2)
    o = bpy.context.active_object
    o.scale = scale
    bpy.ops.object.shade_smooth()
    return apply(o, m)


def cyl(loc, r, h, m, rot=(0, 0, 0), v=20):
    bpy.ops.mesh.primitive_cylinder_add(location=loc, radius=r, depth=h, vertices=v, rotation=rot)
    o = bpy.context.active_object
    bpy.ops.object.shade_smooth()
    return apply(o, m)


def cone(loc, r1, r2, h, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(location=loc, radius1=r1, radius2=r2, depth=h, rotation=rot, vertices=20)
    o = bpy.context.active_object
    bpy.ops.object.shade_smooth()
    return apply(o, m)


def box(loc, scale, m):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.active_object
    o.scale = scale
    return apply(o, m)


def export(name):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    o.location.z = o.dimensions.z / 2
    bpy.ops.object.transform_apply(location=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"),
                             object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True,
                             mesh_smooth_type="FACE")
    print("exported", name)


def pot():
    reset()
    steel = mat("steel", (0.6, 0.6, 0.62), 0.35, 0.9)
    dark = mat("dark", (0.05, 0.05, 0.05), 0.5)
    cyl((0, 0, 0.25), 0.5, 0.5, steel, v=32)
    cyl((0.62, 0, 0.4), 0.14, 0.05, dark, rot=(0, math.pi / 2, 0))
    cyl((-0.62, 0, 0.4), 0.14, 0.05, dark, rot=(0, math.pi / 2, 0))
    export("pot")


def bowl():
    reset()
    w = mat("white", (0.95, 0.95, 0.92), 0.2)
    cone((0, 0, 0.12), 0.1, 0.22, 0.04, w)
    bpy.ops.mesh.primitive_cone_add(location=(0, 0, 0.14), radius1=0.12, radius2=0.28, depth=0.28, vertices=32)
    o = bpy.context.active_object
    apply(o, w)
    export("bowl")


def shrimp():
    reset()
    m = mat("shrimp", (1.0, 0.45, 0.25), 0.4)
    for i in range(5):
        a = i * 0.45
        sphere((i * 0.09 - 0.18, 0, 0.1 + 0.05 * math.sin(a)), (0.07 - i * 0.008, 0.08, 0.07 - i * 0.006), m)
    cone((0.28, 0, 0.1), 0.03, 0.08, 0.1, m, rot=(0, math.pi / 2, 0))
    export("shrimp")


def mushroom():
    reset()
    cap = mat("cap", (0.8, 0.72, 0.58), 0.6)
    stem = mat("stem", (0.95, 0.93, 0.85), 0.6)
    cyl((0, 0, 0.06), 0.035, 0.12, stem, v=12)
    sphere((0, 0, 0.14), (0.09, 0.09, 0.06), cap)
    export("mushroom")


def lemongrass():
    reset()
    m = mat("lg", (0.75, 0.82, 0.45), 0.7)
    cyl((0, 0, 0.15), 0.02, 0.3, m, v=8)
    cone((0, 0, 0.32), 0.02, 0.005, 0.1, m)
    export("lemongrass")


def galangal():
    reset()
    m = mat("gal", (0.9, 0.78, 0.55), 0.7)
    sphere((0, 0, 0.04), (0.1, 0.04, 0.04), m, 12)
    sphere((0.07, 0.02, 0.05), (0.05, 0.03, 0.03), m, 10)
    export("galangal")


def kaffir_lime_leaf():
    reset()
    m = mat("leaf", (0.15, 0.45, 0.15), 0.5)
    sphere((0, 0, 0.005), (0.05, 0.025, 0.004), m, 10)
    sphere((0.07, 0, 0.005), (0.04, 0.02, 0.004), m, 10)
    export("kaffir_leaf")


def chili():
    reset()
    m = mat("chili", (0.85, 0.08, 0.05), 0.35)
    cone((0, 0, 0.05), 0.025, 0.005, 0.14, m, rot=(0, math.pi / 2, 0))
    cyl((-0.08, 0, 0.05), 0.012, 0.03, mat("green", (0.1, 0.5, 0.1)), rot=(0, math.pi / 2, 0), v=8)
    export("chili")


def lime():
    reset()
    m = mat("lime", (0.45, 0.75, 0.15), 0.4)
    sphere((0, 0, 0.05), (0.05, 0.05, 0.045), m)
    export("lime")


def tomato():
    reset()
    m = mat("tomato", (0.9, 0.12, 0.08), 0.35)
    sphere((0, 0, 0.05), (0.06, 0.06, 0.05), m)
    sphere((0, 0, 0.1), (0.02, 0.02, 0.008), mat("tg", (0.1, 0.5, 0.1)), 8)
    export("tomato")


def fish_sauce():
    reset()
    glass = mat("glass", (0.45, 0.22, 0.05), 0.15)
    cap = mat("cap2", (0.8, 0.1, 0.1), 0.4)
    cyl((0, 0, 0.1), 0.05, 0.2, glass, v=16)
    cyl((0, 0, 0.23), 0.02, 0.06, glass, v=12)
    cyl((0, 0, 0.27), 0.025, 0.03, cap, v=12)
    export("fish_sauce")


def counter():
    reset()
    wood = mat("wood", (0.55, 0.38, 0.22), 0.7)
    steel = mat("steel2", (0.7, 0.7, 0.72), 0.3, 0.8)
    box((0, 0, 0.45), (1.0, 0.45, 0.45), wood)
    box((0, 0, 0.92), (1.0, 0.45, 0.02), steel)
    export("counter")


def stove():
    reset()
    steel = mat("steel3", (0.5, 0.5, 0.52), 0.35, 0.8)
    dark = mat("dark2", (0.05, 0.05, 0.05), 0.6)
    box((0, 0, 0.45), (0.5, 0.45, 0.45), steel)
    cyl((0.15, 0.1, 0.92), 0.14, 0.02, dark, v=24)
    cyl((-0.15, -0.1, 0.92), 0.14, 0.02, dark, v=24)
    export("stove")


def person(name, shirt, hair, hat=False):
    reset()
    skin = mat("skin", (0.9, 0.7, 0.55), 0.6)
    shirtm = mat("shirt", shirt, 0.7)
    pants = mat("pants", (0.15, 0.15, 0.3), 0.8)
    hairm = mat("hair", hair, 0.8)
    dark = mat("eye", (0.02, 0.02, 0.02), 0.4)
    cyl((-0.09, 0, 0.225), 0.08, 0.45, pants, v=12)
    cyl((0.09, 0, 0.225), 0.08, 0.45, pants, v=12)
    cyl((0, 0, 0.72), 0.2, 0.55, shirtm, v=16)
    cyl((-0.27, 0, 0.72), 0.06, 0.5, shirtm, v=10)
    cyl((0.27, 0, 0.72), 0.06, 0.5, shirtm, v=10)
    sphere((-0.27, 0, 0.44), (0.06, 0.06, 0.06), skin, 8)
    sphere((0.27, 0, 0.44), (0.06, 0.06, 0.06), skin, 8)
    sphere((0, 0, 1.2), (0.16, 0.16, 0.17), skin)
    sphere((0, 0.02, 1.29), (0.17, 0.17, 0.11), hairm)
    sphere((0, 0.15, 1.33), (0.06, 0.06, 0.06), hairm, 8)      # bun at the back (+Y)
    sphere((0, -0.15, 1.19), (0.03, 0.03, 0.03), skin, 6)      # nose at the front (-Y)
    sphere((-0.06, -0.14, 1.24), (0.02, 0.02, 0.02), dark, 6)
    sphere((0.06, -0.14, 1.24), (0.02, 0.02, 0.02), dark, 6)
    if hat:
        white = mat("hat", (0.97, 0.97, 0.97), 0.8)
        cyl((0, 0, 1.4), 0.19, 0.03, white, v=20)
        cyl((0, 0, 1.49), 0.13, 0.16, white, v=20)
    export(name)


def table():
    reset()
    wood = mat("wood2", (0.6, 0.4, 0.22), 0.7)
    cyl((0, 0, 0.75), 0.55, 0.05, wood, v=32)
    cyl((0, 0, 0.37), 0.05, 0.72, wood, v=12)
    cyl((0, 0, 0.02), 0.28, 0.04, wood, v=24)
    export("table")


def stool():
    reset()
    wood = mat("wood3", (0.5, 0.33, 0.18), 0.7)
    cyl((0, 0, 0.45), 0.2, 0.05, wood, v=20)
    for x in (-0.12, 0.12):
        for y in (-0.12, 0.12):
            cyl((x, y, 0.22), 0.02, 0.44, wood, v=8)
    export("stool")


def plant():
    reset()
    clay = mat("clay", (0.72, 0.35, 0.2), 0.8)
    green = mat("leaf2", (0.12, 0.5, 0.16), 0.6)
    cone((0, 0, 0.15), 0.12, 0.18, 0.3, clay)
    sphere((0, 0, 0.33), (0.17, 0.17, 0.03), mat("soil", (0.15, 0.1, 0.06), 0.9), 12)
    import math as _m
    for i in range(8):
        a = i * 2 * _m.pi / 8
        sphere((_m.cos(a) * 0.09, _m.sin(a) * 0.09, 0.62 + 0.04 * (i % 3)), (0.05, 0.05, 0.28), green, 10)
    sphere((0, 0, 0.75), (0.05, 0.05, 0.3), green, 10)
    export("plant")


def lantern():
    reset()
    red = mat("red", (0.85, 0.08, 0.08), 0.5)
    gold = mat("gold", (0.9, 0.7, 0.2), 0.3, 0.6)
    sphere((0, 0, 0.25), (0.2, 0.2, 0.26), red, 20)
    cyl((0, 0, 0.52), 0.08, 0.04, gold, v=16)
    cyl((0, 0, 0.0), 0.06, 0.04, gold, v=16)
    cyl((0, 0, -0.12), 0.01, 0.2, gold, v=6)
    export("lantern")


def shelf():
    reset()
    wood = mat("wood4", (0.5, 0.35, 0.2), 0.7)
    box((0, 0, 0.5), (0.7, 0.12, 0.02), wood)
    box((-0.5, 0, 0.4), (0.02, 0.1, 0.1), wood)
    box((0.5, 0, 0.4), (0.02, 0.1, 0.1), wood)
    colors = [(0.8, 0.15, 0.1), (0.2, 0.55, 0.2), (0.85, 0.6, 0.15), (0.92, 0.92, 0.88)]
    for i, c in enumerate(colors):
        cyl((-0.5 + i * 0.33, 0, 0.64), 0.06, 0.2, mat("jar%d" % i, c, 0.3), v=14)
    export("shelf")


def sign():
    reset()
    dark = mat("signbg", (0.45, 0.05, 0.05), 0.6)
    gold = mat("signgold", (0.9, 0.7, 0.2), 0.3, 0.6)
    box((0, 0, 0.25), (1.0, 0.03, 0.25), dark)
    box((0, -0.03, 0.5), (1.05, 0.03, 0.03), gold)
    box((0, -0.03, 0.0), (1.05, 0.03, 0.03), gold)
    box((-1.02, -0.03, 0.25), (0.03, 0.03, 0.27), gold)
    box((1.02, -0.03, 0.25), (0.03, 0.03, 0.27), gold)
    export("sign")


for fn in (pot, bowl, shrimp, mushroom, lemongrass, galangal, kaffir_lime_leaf,
           chili, lime, tomato, fish_sauce, counter, stove,
           table, stool, plant, lantern, shelf, sign):
    fn()

person("customer_0", (0.8, 0.15, 0.15), (0.05, 0.04, 0.04))
person("customer_1", (0.15, 0.35, 0.8), (0.35, 0.2, 0.1), hat=True)
person("customer_2", (0.2, 0.6, 0.3), (0.6, 0.6, 0.62))
