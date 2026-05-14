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

### Characters (window walkers)
- **Window Walker** (cube monkey) and **Zombie** (skinned humanoid)
- Recognizes top edges of other windows as ground; walks along them
- Falls when a window moves out from under it — including a proper tumble animation
- Hard landings end in a faceplant + head-shake recovery (zombie only — uses procedural animations)
- Climb animation (zombie) is implemented but not yet triggered by behavior — see roadmap

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

Right-click the tray icon → **Pets** for the 24 cube animals (beaver, bee, bunny, cat, caterpillar, chick, cow, crab, deer, dog, elephant, fish, fox, giraffe, hog, koala, lion, monkey, panda, parrot, penguin, pig, polar bear, tiger) — or **Characters** for the Window Walker / Zombie.

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
