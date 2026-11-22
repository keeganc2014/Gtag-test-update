# Gtag-style map test (offline)

Open this folder in Unity Hub with **Unity 2022.3 LTS** (add the Android Build Support module if you want a Quest build).

1. First open: Unity downloads the XR packages and creates `Assets/Scenes/Main.unity` by itself.
2. Press Play. The scene builds itself at runtime: map, lighting, gorilla rig, menu. Nothing to drag into the scene.
   - No headset: right-mouse-drag = look, WASD = move, Space = jump, Shift = fly, Q/E = platforms, Tab = menu (aim at a button + left click).
3. Quest build (local): menu **Gtag Test > Configure for Quest (Android)** (also enables the Oculus XR loader), then File > Build Settings > Build.
   Or headless: `Unity -batchmode -quit -projectPath . -executeMethod BuildSetup.BuildAndroid` -> `Builds/GtagMapTest.apk`.

## Unity Build Automation (Cloud Build) -> APK
1. Push this folder's *contents* to the root of a Git repo (or set "Project subdirectory" in the build target). `.gitignore` is included.
2. Create a build target: platform **Android**, Unity version **2022.3.20f1** (or the closest 2022.3 LTS offered).
3. Advanced settings -> **Pre-export method name**: `BuildSetup.PreExport`
   (creates the scene, applies IL2CPP/ARM64, Linear color space + Vulkan, assigns the Oculus loader).
4. Build. Download the APK and sideload it to the Quest (`adb install GtagMapTest.apk`).
   No keystore is needed for sideloading; add one in the target's credentials for a Store/release build.

## The stump, the computer and the leaderboard (all offline)
- You spawn in the **stump** (the tree room with the stool, table and monitor).
- The **computer** panel floats in front of the monitor. Tabs: **NAME** (letter keys, DELETE/CLEAR/SAVE NAME), **COLOR** (RED/GREEN/BLUE 0-9 with +/-, RANDOM, live preview), **BOARD** (top 8: rank, color swatch, name, score).
- Your name + color are saved on the device (PlayerPrefs) and your gorilla's fur changes color right away.
- Leaderboard score = time played with that name. Every name you use keeps its own row. It is local only - no online/public lobbies yet.

## Gorilla rig (Gorilla_IK_Rig.blend)
Unity Cloud build machines don't have Blender, so the .blend is kept in `_Originals/` and the game loads an **FBX**:
1. Blender: select the armature `Gorilla_IK_Rig` + mesh `GorillaTag_Model` -> File > Export > FBX. Tick *Selected Objects*, *Apply Scalings: FBX Units Scale*, *Add Leaf Bones: off*, *Bake Animation: off*.
2. Save it as `Assets/Resources/Rig/GorillaRig.fbx`.
3. Done: `GorillaRigAvatar` finds the bones `head`, `upper_arm.L/R`, `forearm.L/R`, `hand.L/R`, drives the head from the headset and runs two-bone IK to the hands. Fur color is applied to the model's materials (`Primary_ColorFur`).
Until the FBX exists, a simple built-in gorilla body (chest + arms + colored hands) is used.
If the hands point the wrong way, change `handRotationOffsetL/R` on the `GorillaRigAvatar` component.

## In VR
- Left X button: open/close the test menu on your left hand. Poke a button with your right hand + pull right trigger.
- Right thumbstick: smooth turn.
- Menu: Speed, Gravity, Fly (right trigger = fly where your right hand points), Noclip, Platforms (grips), Flashlight, Darkness (fog + near-black lighting for horror testing), Go to Spawn, Random Spot, Save/Load Pos.

## Files
- `Assets/Scripts/GorillaLocomotion/` - your Player.cs + Surface.cs (unchanged)
- `Assets/Scripts/GtagBootstrap.cs` - builds the scene, loads the map, spawns the rig with the original prefab's tuning values
- `Assets/Editor/BuildSetup.cs` - Quest/XR build configuration used by the menu, the batch build and Cloud Build
- `Assets/Scripts/XRRigInput.cs` - headset/controller input (+ desktop fallback)
- `Assets/Scripts/TestMenu.cs` - the menu
- `Assets/Scripts/GorillaComputer.cs` - the stump computer (name, color, leaderboard)
- `Assets/Scripts/PlayerProfile.cs` - name/color/leaderboard storage
- `Assets/Scripts/GorillaRigAvatar.cs` - hooks the gorilla rig (or a built-in body) to the player
- `Assets/Resources/Map/` - map FBX + textures (30 textures in your zip; the FBX references a few more that weren't included, those materials stay plain)
- `_Originals/` - your original unitypackage (outside Assets, ignored by Unity)

Poison_Menu.dll is intentionally not in the project: it hooks into the real game's code and can't load in a standalone Unity project.
