# Meshy animation clip plan (action ids from the Meshy catalog, 680 biped presets)

Mikasa: idle=0 run=-1(free) jump=13 land=506 slash=219 combo=92 hit=178
Titan:  idle=0 sprint=509 stomp=255 swipe=97 grab=239 kneel=365 stagger=178 roar=88 death=189

Run: `tools/meshy_rig.py assets/characters/<c>/meshy-raw/task.json assets/characters/<c>/rig <height> name=id ...`
Then merge clips into one GLB with Blender (tools/merge_clips.py, to write) and import into Unity as Humanoid.

## Music
Drop looping tracks into `unity/Assets/Resources/Audio/Music/` as `title`, `battle`, `ending` (mp3/ogg/wav).
`Shared.Music` crossfades between them (2.5 s) based on the title screen and the Titan's state. Missing files are skipped.
