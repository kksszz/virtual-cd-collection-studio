# Portable CD case — glTF 2.0 / GLB

## Portability contract

The exchange file is a standard, self-contained `.glb`. Geometry, UVs, normals, PNG images,
PBR materials, hierarchy and animation clips are ordinary glTF 2.0 data. No Android classes,
Windows paths, audio, third-party STL, external URLs, custom shader code or required extensions
are included. A normal glTF loader can ignore **all** `extras` and still display and animate it.

The root converts the authored decimetre coordinates to glTF metres with scale `[0.1,0.1,0.1]`.
An approximately 14 cm CD case remains correctly sized in other applications, including AR.
The independent mobile case geometry is currently shared with the Windows exporter via the
reproducible `tools/generate-desktop-geometry.ps1` transformation. This is not an export of
the Windows renderer's third-party STL or its lighting environment.

## Standard scene and animations

- Root children: `Case`, `Lid`, `Disc`, `Obi`, `FilmTop`, `FilmBottom`, `Tape`.
- Triangle meshes use FLOAT `POSITION`, `NORMAL`, `TEXCOORD_0`, with embedded buffer views.
- Images are embedded PNGs; materials use core metallic-roughness PBR and `alphaMode=BLEND`
  for transparency. No mandatory transmission/refraction extension is needed.
- Named clips: `Open`, `DiscOut`, `ObiOff`, `WrapOff`, using standard translation, quaternion
  rotation and scale channels. Clips start from the closed/default state; play a clip backwards
  for the corresponding return movement. The application must provide playback controls.
- Portable clips approximate removal fades by shrinking the detached parts; glTF core does not
  animate material opacity. Android retains its existing interruption-safe fade/sequencing.

Generic renderers choose their own lighting/tone mapping/transparent-object sorting, so exact
pixel identity across engines is **not** promised. The disc's procedural rainbow reflection and
film highlights remain renderer-specific. Standard PBR is the portable fallback. Geometry and
artwork are retained; the Android importer uses its existing shading for appearance continuity.

## Optional application metadata (never required to render)

Root `extras.virtualCd` contains `profile: "jewel-case-glb-1"`, `title`, `artist`, `tray`, optional
`obi.frontWidthMm/backWidthMm`, and `wrapped`. Part nodes have `extras.virtualCdPart` (0–6);
materials have `extras.virtualCdFinish` (0 paper, 1 plastic, 2 disc, 3 film). The latter is a
compatibility shading hint, not a substitute for standard material properties.

Image names retain roles: front, insideFront, back, spine, rightSpine, inlay, disc, obiFront,
obiSpine, obiBack. Other software is free to ignore these names. Album binding is a user choice
on import, not a platform-specific URI stored in the GLB.

## Android import scope and compatibility

Android 0.7.13 reads this documented case profile and uses the **stored meshes**, not a procedural
reconstruction. It is not yet a general-purpose importer for arbitrary Blender/glTF scenes:
skinning, indexed/morph geometry, arbitrary node transforms and required extensions are rejected.
The importer maps the documented part roles to the existing interactive case controls; it does
not currently play arbitrary imported animation clips.

Existing `.vcd3d` v1/v2 files and bindings continue to work. The internal binding file retains
its legacy filename for compatibility; file bytes, not the extension, determine the parser.
GLB input is bounded to 32 MiB, JSON 2 MiB, 600,000 vertices, 1,024-pixel PNGs, and no external
resource resolution. Invalid lengths, references, non-finite values and unsupported profiles
are rejected before replacing a saved model. Original music and artwork are never rewritten.

## Verification

- `tools/validate-glb.cjs`: Khronos glTF-Validator, errors/warnings checked.
- `tools/verify-glb-browser.cjs`: Three.js GLTFLoader in local headless Chromium; strips every
  `extras` field first, then checks the standard scene, metre scale and animation, and captures
  closed and disc-out screenshots. Nothing is uploaded to an external viewer.
- Android `-e mode glb`: compare legacy/GLB closed, open and disc-out images on the same GPU
  (mean absolute RGB channel difference <= 1/255), controls, context recreation and malformed
  GLB rejection. `-e mode glbReal` applies this to cache/glb-check.vcd3d and cache/glb-check.glb.
- Synthetic artwork fixtures are in test assets. Actual album artwork remains outside source
  control. The existing Are You Dead Yet? v2 snapshot was converted read-only for validation.

Local test dependencies (not runtime dependencies or release assets): `gltf-validator`
2.0.0-dev.3.10, `three` 0.186.0, `playwright-core` 1.63.0. Each package is extracted under
`.tools/<package>/package`; Chrome is used only for the independent test.
