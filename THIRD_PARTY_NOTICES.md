# Third-Party Notices

CallibriBird (Copyright 2026 Tykhyk Zolotarov, Apache License 2.0) is built on and with the work below. Each part keeps its own license, and these notices must stay with any copy of the project.

## UnityBird: the original Flappy Bird game code

CallibriBird started as a copy of [UnityBird](https://github.com/crosslife/UnityBird) by Ge Ziyang.

The original game is:
- the scripts in `Assets/scripts/` (`BirdControl`, `GameMain`, `PipeSpawner`, `PipeMove`, `LandControl`, `ScoreMgr`, `StartMain`);
- `StartScene`, `GameScene`, the prefabs and the animations.

Tykhyk Zolotarov changed these files in 2026 for muscle control, the difficulty settings and filling any screen. Everything in `Assets/scripts/Emg/`, `ScreenFillTiler` and the session scenes is new.

```
The MIT License (MIT)

Copyright (c) 2014 Ge Ziyang

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NeuroSDK2: the Callibri sensor SDK

- **Location:** `Packages/com.neurotech.neurosdk2/`
- **Copyright:** (c) 2025 BrainBit LLC, MIT License. The full license text is in `Packages/com.neurotech.neurosdk2/LICENSE.md`.
- **Change:** the macOS library in that package was re-signed for this Mac, because BrainBit's signing certificate was revoked. The library itself is unchanged.

## DOTween

- **Location:** `Assets/Plugins/Demigiant/DOTween/`
- **Copyright:** (c) 2014-2026 Daniele Giardini - Demigiant.
- **License:** used under the DOTween license, http://dotween.demigiant.com/license.php

## Art and sounds

The sprites in `Assets/sprites/atlas.png` and the sounds in `Assets/sounds/sfx_*.ogg` came with UnityBird. They appear to be the artwork and sound effects of the original *Flappy Bird* game by Dong Nguyen (.GEARS Studios).

They are **not** covered by the Apache License or the MIT License above, and Tykhyk Zolotarov makes no claim to them. Replace them with original or properly licensed art and sounds before publishing or selling the game.
