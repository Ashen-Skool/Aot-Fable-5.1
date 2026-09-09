# Deploying the web build

`tools/build.sh webgl` writes a **self-contained static site** to `builds/webgl/`. There is no server code,
no build step at the host, and no environment configuration. Upload the folder, serve it, done.

```
index.html
Build/webgl.loader.js
Build/webgl.data.unityweb        (26 MB)
Build/webgl.framework.js.unityweb
Build/webgl.wasm.unityweb        (12 MB)
StreamingAssets/title.mp4
StreamingAssets/nape.mp4
```

Total ~41 MB. `builds/` is gitignored, so the build is never committed - produce it fresh, or hand over a zip.

## What the host has to do

1. **Serve the directory as-is, preserving structure.** `index.html` at the root, `Build/` and
   `StreamingAssets/` as siblings beside it. Relative paths only; the page works from any subdirectory.
2. **Serve over https (or localhost).** `file://` will not work - the loader fetches its own files.
3. That is the whole list.

## What the host does NOT need

- **No `Content-Encoding: gzip` header.** The `.unityweb` files are gzip-compressed, but the build has
  Unity's *decompression fallback* enabled (`webGLDecompressionFallback: 1`), so the loader decompresses in
  JavaScript when the server does not set the header. It works on a plain static host either way. If the host
  *does* set `Content-Encoding: gzip` for `.unityweb`, that is fine too and slightly faster.
- **No COOP/COEP headers.** The build has threads off (`webGLThreadsSupport: 0`), so it does not use
  SharedArrayBuffer and does not need cross-origin isolation.
- **No special MIME types.** `application/octet-stream` for `.unityweb` is fine with the fallback enabled.
- No Node, no framework, no SSR, no database.

## The one thing that can bite

`Build/webgl.data.unityweb` is **26 MB in a single file**. Some hosts cap individual uploads (25 MB is a
common limit). If the host rejects it, the fix is to rebuild with a smaller texture budget
(`Build.WebGLTextures.Run`, see `docs/HARNESS.md`) rather than to split the file - Unity's loader expects one.

## Query flags

The page accepts the harness switches through its query string, which is handy for a demo link:

- `?-autoStart=2` lifts the title automatically after 2 s
- `?-fpslog` prints the frame rate and scene stats to the browser console
- `?-noPost&-noShadows` and the rest of `PerfToggles` for a low-spec fallback link

## Prompt to hand a deploy agent

> Deploy this static site and give me a public URL. It is a Unity WebGL build: `index.html` at the root with
> `Build/` and `StreamingAssets/` beside it. Serve the directory exactly as-is over https, preserving the
> folder structure and all file extensions including `.unityweb`. Do not rewrite, minify, re-compress or
> rename anything, and do not add a build step - it is already built. No headers are required: the build has
> Unity's decompression fallback on, so it does not need `Content-Encoding: gzip`, and it has threads off, so
> it does not need COOP/COEP. The largest file is 26 MB (`Build/webgl.data.unityweb`); if your upload limit
> is below that, tell me rather than splitting or altering the file.
