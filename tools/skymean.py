import bpy, sys
for p in sys.argv[sys.argv.index('--')+1:]:
    img = bpy.data.images.load(p); w,h = img.size; px = img.pixels[:]
    tot=0.0; n=0; totsky=0.0; nsky=0
    for y in range(0,h,4):
        for x in range(0,w,4):
            i=(y*w+x)*4; l=0.2126*px[i]+0.7152*px[i+1]+0.0722*px[i+2]
            tot+=l; n+=1
            if y>h//2 and l<50: totsky+=l; nsky+=1   # upper hemisphere, sun disc excluded
    print("MEAN", p.split('/')[-1], "all %.3f sky %.3f" % (tot/n, totsky/max(nsky,1)))
