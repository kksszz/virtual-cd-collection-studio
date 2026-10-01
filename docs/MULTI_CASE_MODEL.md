# 24 mm multi-case (Windows)

The central plate, rails and hubs share the standard tray material: selecting
White, Black, Gray or Clear applies to both outer trays and the central assembly.
The collection exterior follows the same tray colour selection.

Choose **24mm Front（左右Spine付き）** for a single front insert scan containing
left spine + front panel + right spine. After scanner-margin removal it splits
at 6 : 138 : 6. Back uses the existing Back / Spine付きBack and left/right Spine
roles. Old MultiBack role keys remain readable aliases, without duplicate UI entries.
Individual front spine assignments override the corresponding front crop.

The normal **ケース** selector now includes **24mmマルチケース**. Use the
existing **用途** selector to persist image mappings, then inspect the result
in the normal 3D viewer. See Windows viewer integration below for controls
and the currently unsupported operations.

The former **24mm試作** toolbar button has been removed. The isolated geometry
preview is retained only for developer tests, not as a user-facing entry point.
The two-stage slider first opens the front outer shell, then turns the central
assembly over. Rotation, zoom and disc visibility are independently adjustable.

## Measured specimen (mm)

| Part | Dimension |
|---|---:|
| Central frame overall width, including end projections | 141.5 |
| Central frame overall height | 124 |
| Central plate thickness | 1 |
| Rail reach from either plate face | 9.5 |
| Main rail depth | 20 |
| Hinge tab depth | 24 (2 extra on each side) |
| Hinge axis spacing | 14 |
| Axis to projecting end | 5 |

The nominal case envelope is 142 × 124 × 24 mm. Side scans and the supplied
opening video establish two parallel axes and four disc seats: one in each
outer half and one on either face of the central assembly.

## Reuse and provisional geometry

Both outer halves reuse the existing standard case's **disc-side** tray and
clear shell, fitted to the 124 mm height without scaling their X/Z dimensions.
The original well/hub relief and procedural vertical spine ribs, groove floor
and raised shoulder are reused, not just the STL tray. Rear faces are positioned
at the 24 mm envelope; the outer discs share the standard tray's seating transform. The
central retaining hubs reuse triangles from the standard tray. Existing STL
attribution and licensing remain applicable; see the project's third-party
notices. The front booklet lid is not used.

Corner cut-outs follow a 256-segment circular contour clipped by a straight
diagonal edge, replacing the visibly stepped 1 mm occupancy grid. Both faces
and the 1 mm hole walls share the contour. Hole positions and radii remain
scan-based approximations; this change improves smoothness, not measurement accuracy.
The tab length (8 mm), pin diameter (1.5 mm), rib spacing and outer shell
seating are provisional. These are not claims of manufacturing accuracy.
The tab outline now has circular 1.2 mm corner radii and the far rail end
0.8 mm radii, estimated from img330. These replace square box corners without
changing the overall dimensions or hinge axes; exact radii remain provisional.
The projecting end also has a rounded central notch through the rail thickness,
estimated from img330 at 6 mm wide and 1.2 mm deep. Both upper/lower rails use
the same profile; these notch dimensions are provisional, not measured.
Side scans img334/335 additionally establish an opaque black central side wall
between the two outer inlay folds. The prototype estimates this black band at
7 mm and adds an opaque backing and blank 6 mm paper fold to each outer half
on both sides. The original scans are reference only and are not redistributed
or used as textures. Exact side-wall depth and latch relief remain provisional.
The right/free side now has a smooth finger recess on its front-facing edge,
based on the close-up photo and img332/334: provisional 20 mm span, 1.8 mm
edge drop and 0.6 mm inward bow. The rear edge and opposite hinge side remain
unchanged. This is actual mesh geometry, not a shaded texture.
Side-view GPU tests check that coloured discs are occluded in the closed pose.

## Preview artwork mapping

The prototype now loads the selected album's existing folder/ZIP/managed images
and stored rotations. The right-hand panel selects front/back jackets, four spine
folds and Disc 1–4 independently. Existing roles seed the selectors; unassigned
discs are hidden. Changes apply with `画像を適用` and are not saved.
Each slot supports additional 90-degree rotations. Jackets can use automatic
fold detection, panel-only mode, or a forced 6+138+6 mm full-insert split. Explicit
spine selections override derived folds. No source image or artwork role is written.
The front/back panels are provisional 138 × 117.5 mm planes and move with their
outer halves; the four disc labels move with their respective trays.

## Windows viewer integration

Choose `24mmマルチケース` in the existing case selector (saved as `Multi24`).
The regular image `用途` selector includes dedicated full-insert and panel-only
Front roles and two front spine roles. Back uses the standard definitions.
`Disc1`, `Disc2`, `Disc3`, `Disc4` independently assign the front tray, central
front, central reverse and rear tray. Old `Disc` assignments alias Disc1.
Unassigned discs have no 3D mesh (the empty tray remains). If and only if
Disc1 and Disc2 are assigned and Disc3/4 are absent, Disc2 uses the rear tray
instead of the central front seat. Other partial assignments retain their seats.
The separate Front / FrontSpread booklet lies on the central front tray, visible
on the right at first opening as in the supplied photo. FrontInside supplies its
reverse. The 24mm Front insert remains the exterior jacket and is not used as a
booklet fallback. The booklet follows the central tray when turned; its nominal
120 mm square size and 0.4 mm thickness are provisional. A disc assigned under
the booklet remains present but is covered. No extraction animation is added.
The booklet is shifted 9 mm right (X = -51..69 mm) to align with the inner
right frame, without stretching the cover. Its reverse and paper body move together.
Disc2 now means one disc image, not a two-disc composite scan; composite scans
must be separated before assigning the individual numbered roles.
Existing Front/spread assignments remain fallback inputs.
These normal role assignments and existing image rotations persist per album.
The old separate prototype is no longer accessible from the artwork toolbar.

The normal 3D viewer opens the front with `ケースを開く`, then uses
`中央をめくる` / `中央を戻す` to expose Disc 3–4 / Disc 1–2. The library rack
uses a lightweight 24 mm closed exterior. Standard and digipak models retain
their existing controls. Booklet reading remains available when existing page
assignments provide it, without a 24 mm extraction animation.

Spine Card and Spine Card reverse use existing scans and manual fold settings.
The obi wraps around 24 mm depth with 120 mm height; flaps retain scan aspect.
It can slide off, be dragged and reinserted after closing. Opening removes it
and moves it clear of the front half. The collection exterior also displays it.
Transparent wrapping is not enabled for this format.

Click an exposed CD to select it; the lower-right DiscN button extracts/returns
only that CD. Extracted CDs can be dragged independently. Closing returns them.
Double-click plays/stops tracks for that logical Disc number and only that
activated CD rotates. Disc numbers must be present in the audio metadata;
missing matching tracks report a message instead of playing a different disc.
The two-disc layout retains Disc2 identity at the rear seat. Hidden/unassigned
or booklet-covered discs cannot be selected. Extraction/selection survives
same-album artwork refresh and resets for another album.

Wrapping remains unsupported for this format. Windows now exports the measured
model as `multi-case-24mm-glb-1`; the matching Android development build reads
that GLB and provides opening, central turning, optional Spine Card removal,
and separate Disc1–Disc4 extraction controls. Older Android builds reject the
new profile. Booklet extraction is not yet animated on Android.
