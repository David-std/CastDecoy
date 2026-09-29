<div align="center">

<img src="Assets/brand.png" alt="CastDecoy Banner" width="100%" />

# CastDecoy

**Experimental Display Isolation, Capture Exclusion, and Media Stream Simulation Lab**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D6?style=for-the-badge&logo=windows)](https://microsoft.com)
[![Framework](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)
[![Architecture](https://img.shields.io/badge/Architecture-Win32%20Native%20%2B%20WASAPI-10b981?style=for-the-badge)](https://github.com)

</div>

---

## Overview

CastDecoy is an experimental laboratory utility designed for Windows display isolation, screen capture exclusion, and multimedia stream simulation. It provides a testing suite to explore how window display affinities, display properties, cursor events, camera feeds, and audio signals interact with screen capture engines and desktop conferencing software (such as Zoom, Microsoft Teams, Google Meet, Discord, and OBS Studio).

By leveraging native Win32 APIs and Desktop Window Manager (DWM) composition, CastDecoy allows testing window capture exclusion in real time. Excluded windows remain fully interactive on the local monitor while remaining hidden from screen capture pipelines.

---

## Installation and Packages

| Distribution | File | Details |
| :--- | :--- | :--- |
| **Setup Installer** | `CastDecoy-Setup-v1.0.0.exe` | Standard Windows setup with Desktop and Start Menu shortcuts, and registration in Windows Installed Apps. |
| **Portable Package** | `CastDecoy-v1.0.0-win-x64.zip` | Self-contained archive. Extract and run directly without requiring .NET SDK or administrative privileges. |
| **1-Click Script** | `installer/Install.cmd` | Automated local deployment script for quick setup. |

### Quick Start
1. Download **`CastDecoy-Setup-v1.0.0.exe`** from the [Releases](https://github.com/David-std/CastDecoy/releases) section.
2. Run the installer and click **Install Now**.
3. CastDecoy installs to `%LocalAppData%\Programs\CastDecoy` with clean uninstallation support via *Windows Settings > Installed Apps*.

---

## Key Modules

### 1. Window and Capture Isolation (WDA)
- **Hardware-Level Capture Exclusion**: Leverages `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE` to conceal browser windows (Chrome, Edge, Brave, Firefox) or confidential applications from recordings without altering local interaction.
- **Taskbar Presentation Control**: Selectively hides protected application icons from the system taskbar via the native `ITaskbarList` interface.
- **Live DWM Thumbnails**: High-performance window previews powered by Desktop Window Manager APIs with minimal CPU overhead.
- **Display Environment Emulation (ScreenSpoof)**: Chromium launch profile and emulation hooks for testing multi-screen window placement and single-display behavior.

### 2. Mouse Decoy and Input Simulation
- **Trajectory Recording and Playback**: Record physical mouse paths or draw custom trajectories using the integrated interactive canvas (freehand, lines, rectangles, circles) to simulate input activity.
- **Cursor Freeze**: Lock the broadcast cursor position at designated coordinates while continuing to use the physical mouse unhindered.
- **Latency and Packet Loss Simulation**: Emulate network jitter, coordinate hops, and lagged ghost replicas for stream testing.
- **Simulated Natural Motion**: Generates smooth micro-oscillations modeling human hand motion patterns to test activity-sensing mechanisms and prevent idle timeouts.

### 3. Camera Decoy and Signal Simulation
- **Direct Hardware Video Capture**: Acquires real-time physical camera video via FlashCap at native resolution and adjustable framerates (1 to 60 FPS).
- **Instant Frame Freeze**: Locks the transmitted video frame on demand while keeping the active hardware connection alive in video call clients.
- **Intermittent Signal Degradation**: Simulates packet dropouts, frame drops, and periodic stutter cycles.
- **In-Memory Video Loop Buffer**: Continuously records and loops rolling buffers of 5, 10, 15, 30, or 60 seconds.
- **Test Pattern Generation**: Emits solid standby colors or simulated sensor distortion modes (horizontal blanking, noise bands).

### 4. Audio Decoy and Acoustic Processing
- **Low-Latency Digital Pipeline**: Operates at 48,000 Hz / 24-bit with ultra-low latency (< 5 ms) powered by NAudio.
- **Carrier Stream Mute**: Mutes outgoing microphone content while keeping hardware sessions active, maintaining an active audio carrier signal.
- **Programmable Audio Degradation**:
  - *Choppy Voice*: Simulates packet drop rates (25%, 50%, 75%).
  - *Dual Echo and Reverb*: Injects room acoustic reflection or speaker feedback.
  - *White Noise and Static*: Adds 50-60 Hz electrical hum or analog cable hiss.
  - *Extreme Gain Saturation*: Drives signal into simulated hardware clipping.
- **Real-Time Dual Spectrum Analyzer**: Live visualization of raw analog input frequencies versus processed output signals.

### 5. Global Hotkeys and System Integration
- **System-Wide Hotkeys**: Registered through the Win32 message loop (`RegisterHotKey`) for immediate execution even when the application is minimized.
- **Per-Application Hotkeys**: Quickly toggle individual window visibility using custom key combinations.
- **System Tray Support**: Minimizes cleanly to the Windows notification area with background persistence.

---

## System Requirements

- **Operating System**: Windows 10 (Build 1903 or higher) or Windows 11 (64-bit).
- **Privileges**: Standard user privileges (administrative elevation may be required when targeting elevated system processes).
- **Build Environment**: .NET 10.0 SDK with Windows Desktop workload (only required if building from source).

---

## Building from Source

1. **Clone the repository**:
   ```bash
   git clone https://github.com/David-std/CastDecoy.git
   cd CastDecoy
   ```

2. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

3. **Build Release binary**:
   ```bash
   dotnet build -c Release
   ```

4. **Publish self-contained single-file executable (win-x64)**:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o bin/publish/win-x64
   ```

---

## Research and Experimental Use

CastDecoy is an experimental laboratory utility developed for testing, research, and technical demonstrations of Windows graphics affinity and multimedia streaming pipelines. It is not an official tool or endorsed product. Users and developers are responsible for ensuring all usage complies with applicable organizational policies, institutional rules, and third-party terms of service.

---

## License

This project is licensed under the terms of the [MIT License](LICENSE).
