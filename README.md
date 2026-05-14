# DesktopPet

A small wandering 3D animal that lives on your Windows desktop and chases your cursor.

![DesktopPet preview](kenney_cube-pets_1.0/Previews/animal-cat.png)

## What it does

- Floats above all other windows, click-through so it never gets in your way
- Wanders the screen on its own (idle / walk / sleep state machine)
- Chases your cursor when it strays too far, pauses briefly when it catches it
- Rotates smoothly to face its movement direction
- System tray icon to exit

## Installing (end users)

Grab `DesktopPet-Setup-<version>.exe` from [Releases](https://github.com/SixOfFive/DesktopPet/releases) and run it. The installer is self-contained — no .NET runtime required on the target machine.

During install you can opt in to:
- A desktop shortcut
- **Launch on Windows startup** (creates a shortcut in your user Startup folder)

Right-click the tray icon → **Exit** to quit. Uninstall from Settings → Apps as usual.

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

The Kenney Cube Pets pack ships 24 animals. By default the app loads `animal-cat.glb`. To use a different one, drop into [`Program.cs`](Program.cs) and change the preferred filename, or simply remove `animal-cat.glb` from `bin/.../pets/` — the app will pick the first `.glb` alphabetically.

Available models: beaver, bee, bunny, cat, caterpillar, chick, cow, crab, deer, dog, elephant, fish, fox, giraffe, hog, koala, lion, monkey, panda, parrot, penguin, pig, polar (bear), tiger.

## How it works

**Rendering pipeline** (every 16 ms):

```
Pet state machine → 3D scene → offscreen FBO → glReadPixels → UpdateLayeredWindow
```

A hidden Silk.NET OpenGL 3.3 context renders the model into a 128×128 framebuffer with a basic lambert shader. The frame is read back, Y-flipped, alpha-premultiplied, and pushed onto a `WS_EX_LAYERED | WS_EX_TRANSPARENT` window via `UpdateLayeredWindow` — giving true per-pixel transparency without any GPU-window-compositing headaches.

**Source layout**

| File | Role |
|---|---|
| [Program.cs](Program.cs) | Entry point — initializes GL, scene, tray, form |
| [Pet.cs](Pet.cs) | State machine (Idle / Walk / Sleep / Chase), smooth yaw turning |
| [LayeredPetForm.cs](LayeredPetForm.cs) | Transparent click-through topmost window, `UpdateLayeredWindow` plumbing |
| [TrayIcon.cs](TrayIcon.cs) | System tray icon |
| [Renderer/GlHost.cs](Renderer/GlHost.cs) | Hidden Silk.NET window + OpenGL 3.3 context |
| [Renderer/Scene.cs](Renderer/Scene.cs) | FBO, shader, render-and-readback loop |
| [Renderer/GltfLoader.cs](Renderer/GltfLoader.cs) | SharpGLTF → GPU vertex buffers, base color textures |
| [Renderer/Shader.cs](Renderer/Shader.cs) | Shader program wrapper, uniform cache |
| [Renderer/Mesh.cs](Renderer/Mesh.cs) | VAO/VBO/EBO + draw call |
| [Renderer/Texture.cs](Renderer/Texture.cs) | GL texture wrapper |

## Dependencies

- [Silk.NET](https://github.com/dotnet/Silk.NET) — OpenGL bindings, hidden window
- [SharpGLTF](https://github.com/vpenades/SharpGLTF) — glTF 2.0 / GLB loading
- WinForms — host window, system tray, timer

## Roadmap

- [ ] Skeletal animation playback (idle/walk loops from the GLB animation channels)
- [ ] Multi-monitor support (use `SystemInformation.VirtualScreen`)
- [ ] Click-to-pet interaction (handle mouse events without breaking click-through)
- [ ] Tray menu: pick a different pet at runtime
- [ ] High-DPI awareness — render at monitor pixel size, not fixed 128
- [ ] Settings file for tunables (speed, notice distance, render size)

## Credits & licenses

- **Kenney Cube Pets pack** — Created and distributed by [Kenney](https://kenney.nl) under **CC0 1.0** (public domain). Included in [`kenney_cube-pets_1.0/`](kenney_cube-pets_1.0/) with the original `License.txt`.
- **Project code** — see repo license.
