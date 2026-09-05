import bpy, sys, math
paths = sys.argv[sys.argv.index('--')+1:]
for p in paths:
    img = bpy.data.images.load(p)
    w, h = img.size
    px = img.pixels[:]  # RGBA, row 0 = bottom
    best = -1; bi = 0
    for i in range(0, w*h):
        lum = px[i*4] + px[i*4+1] + px[i*4+2]
        if lum > best: best = lum; bi = i
    y, x = divmod(bi, w)
    u = (x + 0.5) / w; v = (y + 0.5) / h
    phi = (0.5 - u) * 2 * math.pi           # atan2(z, x)
    az = (90.0 - math.degrees(phi)) % 360.0  # atan2(x, z)
    if az > 180: az -= 360
    lat = (1 - v) * math.pi
    el = 90.0 - math.degrees(lat)
    print("SUN", p.split('/')[-1], "px", x, y, "az %.1f el %.1f lum %.1f" % (az, el, best))
