# DesktopPet

A small wandering 3D pet that lives on your Windows desktop. Comes with cube animals that roam the screen and chase your cursor, plus a window-aware zombie that walks along your window edges and tumbles when the floor disappears.

![DesktopPet preview](kenney_cube-pets_1.0/Previews/animal-cat.png)

## What it does

**Two flavors of pet, switchable from the tray menu:**

### Cube Pets (24 animals)
- Floats above all other windows, click-through so it never gets in your way
- Wanders the screen on its own (idle / walk / sleep state machine)
- Chases your cursor when it's moving and far away — pauses briefly when it catches it
- Once a minute, a catch triggers `eat` + `dance` animations
- After 60 seconds of cursor idle, walks back to the cursor and lies down to sleep
- Sleeping pet gets floating animated Z's and occasional dream-twitches

### Characters
- **Zombie** (skinned humanoid)
- Recognizes top edges of other windows as ground; walks along them
- Falls when a window moves out from under it — tumble animation while airborne
- Hard landings end in a faceplant + head-shake recovery (procedural animations)
- Climb animation is implemented but not yet triggered by behavior — see roadmap

## Installing (end users)

Grab `DesktopPet-Setup-<version>.exe` from [Releases](https://github.com/SixOfFive/DesktopPet/releases) and run it. The installer is self-contained — no .NET runtime required on the target machine.

During install you can opt in to:
- A desktop shortcut
- **Launch on Windows startup** (creates a shortcut in your user Startup folder)

Right-click the tray icon → **Exit** to quit. Uninstall from Settings → Apps as usual.

The pet auto-scales to your screen height — roughly 18% of vertical pixels, rounded to a multiple of 16, clamped to [96, 512]. 1080p gets ~192 px, 1440p ~256 px, 4K ~384 px.

## Running from source

Requires the **.NET 9 SDK**.

```powershell
dotnet run
```

## Building the installer

Requires **Inno Setup 6** ([download](https://jrsoftware.org/isdl.php) or `winget install JRSoftware.InnoSetup`).

```powershell
pwsh -File build-installer.ps1
```

This runs `dotnet publish` then `iscc.exe installer.iss`. Output: `dist\DesktopPet-Setup-<version>.exe` (~35 MB).

## Building just the executable

```powershell
dotnet publish -c Release
```

Output: `bin\Release\net9.0-windows\win-x64\publish\Neko.exe` — self-contained portable build (drag the whole `publish/` folder anywhere and run).

## Picking a different pet

Right-click the tray icon → **Pets** for the 24 cube animals (beaver, bee, bunny, cat, caterpillar, chick, cow, crab, deer, dog, elephant, fish, fox, giraffe, hog, koala, lion, monkey, panda, parrot, penguin, pig, polar bear, tiger) — or **Characters → Zombie** to switch to the window-walking humanoid.

## Custom textures

Every cube pet samples colors from a shared 512×512 palette (`colormap.png`). You can recolor one pet without affecting the others by dropping a PNG named `<modelname>-skin.png` in the [`skins/`](skins/) folder — the build copies it next to the GLB and the renderer uses it instead of the default colormap.

Two bundled skins (both modeled on the author's real pets):

- **Cat** (`skins/animal-cat-skin.png`) — long-haired brown tabby with cream belly tones. Modifies cells (3,3) and (3,2) of the palette.
- **Dog** (`skins/animal-dog-skin.png`) — Rottweiler-style black-and-tan. Most cells filled near-black for the body; cell (1,2) holds the rust-tan that the leg UVs sample.

To make your own:
1. Copy `kenney_cube-pets_1.0/Models/GLB format/Textures/colormap.png` to `skins/<modelname>-skin.png`.
2. Open it in any image editor (it's 512×512, a 4×4 grid of 128×128 cells).
3. Modify the cells the model uses (each pet only samples a couple of them — look at the GLB's UV ranges or just edit-and-iterate).
4. Rebuild and switch to that pet from the tray.


## How it works

**Rendering pipeline** (every 16 ms):

```
Pet state + behavior → 3D scene → offscreen FBO → glReadPixels → UpdateLayeredWindow
```

A hidden Silk.NET OpenGL 3.3 context renders the model into an offscreen framebuffer. The frame is read back, Y-flipped, alpha-premultiplied, and pushed onto a `WS_EX_LAYERED | WS_EX_TRANSPARENT` window via `UpdateLayeredWindow` — giving true per-pixel transparency without GPU-window-compositing headaches.

**Two model paths share the same Scene:**
- **Rigid-node animation** (cube pets) — each leg/tail/body is its own mesh on its own node, animation channels move the nodes. Built with a simple lambert vertex shader.
- **Skinned mesh animation** (zombie) — single mesh with per-vertex joint indices and weights, 45-bone palette uploaded as a uniform array, skinning math in a second vertex shader. Also supports **procedural animations** layered on top of rest-pose TRS for clips not in the source pack (Fall, FacePlant, HeadShake, Climb).

**Behaviors are pluggable** (`IPetBehavior`):
- `Pet` (FreeWander) — random idle/walk + cursor chase
- `WindowWalker` — gravity-based physics + ground search via `EnumWindows` + `GetWindowRect`

**Source layout**

| File | Role |
|---|---|
| [Program.cs](Program.cs) | Entry point — discovers models, builds tray groups, wires events |
| [Pet.cs](Pet.cs) | FreeWander state machine, cursor chase, sleep cycle, Z-particle hooks |
| [WindowWalker.cs](WindowWalker.cs) | Gravity, ground search, faceplant recovery |
| [WindowEnumerator.cs](WindowEnumerator.cs) | Win32 `EnumWindows` + cloak/minimize/size filters |
| [LayeredPetForm.cs](LayeredPetForm.cs) | Transparent click-through topmost window, `UpdateLayeredWindow`, auto-scale RenderSize |
| [TrayIcon.cs](TrayIcon.cs) | System tray with grouped submenus (Pets / Characters) |
| [ZParticles.cs](ZParticles.cs) | Floating Z's overlay drawn on the bitmap during sleep |
| [Renderer/GlHost.cs](Renderer/GlHost.cs) | Hidden Silk.NET window + OpenGL 3.3 context |
| [Renderer/Scene.cs](Renderer/Scene.cs) | FBO, both shader paths, render-and-readback, state→clip mapping |
| [Renderer/GltfLoader.cs](Renderer/GltfLoader.cs) | Cube-pet (rigid-node) loader, SharpGLTF → GPU |
| [Renderer/SkinnedLoader.cs](Renderer/SkinnedLoader.cs) | Humanoid loader, multi-GLB animation merging, skin texture |
| [Renderer/AnimatedModel.cs](Renderer/AnimatedModel.cs) | Rigid-node model + sampler-based animation playback |
| [Renderer/SkinnedModel.cs](Renderer/SkinnedModel.cs) | Skinned model + bone palette + procedural animation dispatch |
| [Renderer/ProceduralAnimations.cs](Renderer/ProceduralAnimations.cs) | Fall, Climb, FacePlant, HeadShake — direct bone TRS manipulation |
| [Renderer/Shader.cs](Renderer/Shader.cs) | Shader program wrapper, uniform cache |
| [Renderer/Mesh.cs](Renderer/Mesh.cs) | Cube-pet VAO/VBO/EBO |
| [Renderer/Texture.cs](Renderer/Texture.cs) | GL texture wrapper |

## Dependencies

- [Silk.NET](https://github.com/dotnet/Silk.NET) — OpenGL bindings, hidden window
- [SharpGLTF](https://github.com/vpenades/SharpGLTF) — glTF 2.0 / GLB loading and animation curves
- [FBX2glTF](https://github.com/facebookincubator/FBX2glTF) (build-time only) — converted Kenney's retro-characters FBX into GLB
- WinForms — host window, system tray, timer

## Roadmap

- [ ] Climb behavior — detect vertical window edges, climb up them, fall when out of grip (animation already in place)
- [ ] More skin variants (human male/female, zombie female) as separate tray entries
- [ ] Multi-monitor support (use `SystemInformation.VirtualScreen`)
- [ ] Click-to-pet interaction (handle mouse events without breaking click-through)
- [ ] Settings file / tray menu for size, speed, notice distance
- [ ] Mixamo animation grafting for richer character clips

## Credits & licenses

All bundled 3D assets are **CC0 1.0** (public domain) — free for personal, educational, and commercial use without attribution required. Credits provided here as courtesy.

- **Kenney Cube Pets** — Created and distributed by [Kenney](https://kenney.nl) under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/). Included in [`kenney_cube-pets_1.0/`](kenney_cube-pets_1.0/) with the original `License.txt`. Source: <https://kenney.nl/assets/cube-pets>.
- **Kenney Animated Characters Retro** — Created and distributed by [Kenney](https://kenney.nl) under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/). The Zombie character (mesh + idle/jump/run animations + skin textures) is from this pack. Included in [`kenney_animated-characters-retro/`](kenney_animated-characters-retro/) with the original `License.txt`. Source: <https://kenney.nl/assets/animated-characters-retro>.
- **Project code** — see repo license.
