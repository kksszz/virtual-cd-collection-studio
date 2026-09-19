# Third-party notices

Virtual CD Collection Studio's original source code is licensed under
GNU General Public License version 3 only. The components and assets below
remain subject to their respective licenses.

## QRCoder 1.7.0

- Project: https://github.com/Shane32/QRCoder
- Copyright © 2013-2025 Raffael Herrmann; © 2024-2025 Shane Krueger.
- License: MIT; included text: `THIRD_PARTY_LICENSES/QRCoder-MIT.txt`.
- Used for offline connection QR generation on Windows.

## ZXing Android Embedded 4.3.0 / ZXing Core 3.4.1

- Projects: https://github.com/journeyapps/zxing-android-embedded and https://github.com/zxing/zxing
- License: Apache License 2.0; https://github.com/journeyapps/zxing-android-embedded/blob/v4.3.0/COPYING
- Used for on-device QR decoding and camera lifecycle; no camera images are uploaded by the sync feature.

## FLAC CUE playback: FlakeNAudioAdapter 1.0.2 / CUETools.Codecs.FLAKE-Reloaded 1.0.1

- Adapter: https://github.com/teekay/FlakeNAudioAdapter, Copyright (c) 2022 Tomáš Kohl.
- Adapter license: MIT, `THIRD_PARTY_LICENSES/FlakeNAudioAdapter-MIT.txt`.
- Decoder source: https://github.com/teekay/FLACTools, Copyright 2008–2010 Grigory Chudov, 2022 Tomáš Kohl.
- Decoder license: LGPL 2.1, `THIRD_PARTY_LICENSES/CUETools-FLAKE-LGPL-2.1.txt`.
- Unmodified dynamically linked assemblies used for sample-accurate FLAC CUE seeking.

## NAudio 2.2.1

- Project: https://github.com/naudio/NAudio
- Copyright: Copyright 2020 Mark Heath
- License: MIT License
- Included license text: `THIRD_PARTY_LICENSES/NAudio-MIT.txt`

## TagLibSharp 2.3.0

- Project: https://github.com/mono/taglib-sharp
- License: GNU Lesser General Public License v2.1 only (LGPL-2.1-only)
- Included license text: `THIRD_PARTY_LICENSES/TagLibSharp-LGPL-2.1.txt`

## HelixToolkit.Wpf.SharpDX 3.1.2

- Project: https://github.com/helix-toolkit/helix-toolkit
- Copyright: Copyright (c) 2023 Helix Toolkit contributors
- License: MIT License
- Included license text: `THIRD_PARTY_LICENSES/HelixToolkit-MIT.txt`

## CD, DVD case

The jewel-case geometry includes modified versions of the following model:

- Work: **CD, DVD case**
- Creator: **Moder (@MHKK_1419475)**
- Source: https://www.printables.com/model/647946-cd-dvd-case
- License: Creative Commons Attribution 4.0 International (CC BY 4.0)
- License text: https://creativecommons.org/licenses/by/4.0/
- Included license text: `THIRD_PARTY_LICENSES/CdCase-CC-BY-4.0.txt`

Changes made for Virtual CD Collection Studio include coordinate normalization,
non-uniform depth scaling, separation into lid/perimeter/tray rendering groups,
material replacement, and adaptation for real-time textured rendering.

The original creator does not endorse this application.
