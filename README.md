# RemotePad for Windows

RemotePad turns keyboard and mouse input into a virtual Xbox 360 controller. Use it on Windows with Xbox Remote Play in a browser or with chiaki-ng for PlayStation 5. You can rebind every controller action, adjust mouse sensitivity, and invert the vertical camera axis.

## Quick start

1. Download the Windows x64 ZIP from [Releases](https://github.com/Morf192/RemotePad/releases). Extract the entire ZIP.
2. Install [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0) and [ViGEmBus 1.22.0](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0). ViGEmBus requires administrator rights to install and is no longer maintained.
3. Run `win-x64\RemotePad.exe`. The virtual controller connects immediately, and input starts paused.
4. Open [Xbox Remote Play](https://www.xbox.com/remoteplay) or start a session with your PS5 in [chiaki-ng](https://chiaki-ng.github.io/chiaki-ng/setup/configuration/). If the Xbox browser tab is waiting for controller input, press **F8** and then **Space** while the tab is active.
5. Press **F8** to enable or pause emulation. Assigned keyboard keys and mouse buttons are captured across Windows while emulation is enabled. Pause before typing or using the desktop.

## Standard controls

| Input | Xbox 360 action |
| --- | --- |
| W / A / S / D | Left stick (movement) |
| Mouse movement or arrow keys | Right stick (camera) |
| I / J / K / L | D-pad |
| Space / X / F / R | A / B / X / Y |
| Left click / Right click | LT / RT |
| Q / E | LB / RB |
| Shift / Middle click | L3 / R3 |
| Tab / Enter | View / Menu |
| Home | Guide |
| F8 | Enable or pause emulation |

Open **Controls…** and double-click an action to rebind it. Select **Standard** to restore the default layout and mouse settings, then select **Save** to apply your changes. Opening the controls window pauses emulation.

On PS5, chiaki-ng maps the Xbox gamepad to PlayStation controls. Use a separate binding in chiaki-ng if you need the DualSense touchpad. [chiaki-ng controller options](https://chiaki-ng.github.io/chiaki-ng/setup/controlling/)

## Build from source

The full C# source and tests are in [src](src). You can also download the separate source ZIP from the release page. With the .NET 8 SDK installed, run these commands from the repository root.

```powershell
dotnet publish src/RemotePad.csproj -c Release -r win-x64 --self-contained false -o publish
dotnet run --project src/tests/RemotePad.Tests.csproj -c Release
```

On Windows, you can also run `RemotePad.exe --self-test` or `RemotePad.exe --driver-test`. The driver test requires ViGEmBus and a free XInput slot. Automated checks cover layout persistence, mouse bindings, and virtual controller input through Windows XInput. GTA VI gameplay through Remote Play has not been verified.
