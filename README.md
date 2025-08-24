# 🎮 MobControl — Mobile as a Game Controller

MobControl is a Final Year Project (FYP) that transforms an Android smartphone into a flexible game controller for PC games.  
It consists of two main applications:

- **📱 Mobile App** (Unity 6000.0.37f1, Android)  
- **🖥️ Desktop App** (WPF, .NET 8, Windows)  

Together, they provide **seamless QR-based pairing**, **offline WebSocket communication**, and **customizable layouts** for multiple players.

---

## 📂 Repository Structure

```
mobcontrol/
├─ mobile-app/       # Unity project (Android build)
├─ desktop-app/      # WPF / .NET 8 project (Windows)
├─ docs/             # Documentation
│  ├─ final-user-manual.pdf
│  ├─ setup-guide.md
│  └─ architecture.md
├─ .gitignore
├─ .gitattributes
├─ LICENSE
└─ README.md
```

---

## 🚀 Quick Start

### 1. Desktop App (Windows, .NET 8)
1. Navigate to `desktop-app/src/`  
2. Build & run:
   ```bash
   dotnet restore
   dotnet build -c Release
   dotnet run -c Release
   ```
3. The Desktop App will launch a WebSocket server on port **8181**.  
4. Ensure your firewall allows inbound connections on this port.

### 2. Mobile App (Unity 6000.0.37f1, Android)
1. Open `mobile-app/` in Unity Hub.  
2. Build for Android (**File → Build Settings → Android → Build**).  
3. Install the APK on your Android device.  
4. Ensure the mobile device and desktop are on the **same network**.  

### 3. Pairing
- Open the Desktop App → show QR code.  
- On Mobile App → open **Pairing Page** → scan QR or enter token.  
- Once paired, use the controller page to send inputs.

---

## 📖 Documentation

- 📚 [Setup Guide](./docs/setup-guide.md) — step-by-step installation  
- 🏗️ [Architecture Overview](./docs/architecture.md) — high-level design & flow  
- 📄 [User Manual](./docs/User%20Manual.pdf) — detailed usage  

---

## 🔐 Key Features
- Seamless **QR-based pairing** with unique session tokens  
- Works **offline on local LAN** using WebSocket
- Supports multiple mobile devices with isolated sessions  
- Flexible **JSON-based controller layouts**  
- Future support for accelerometer/motion input  

---

## 📦 Releases
Stable builds (APK + Desktop installer) are published under [GitHub Releases](../../releases).  
- `MobControl-Desktop-Setup-1.0.0.exe` (Windows installer)
- `MobControl-Desktop-1.0.0.msi` (Windows installer)
- `MobControl-Mobile-1.0.0.apk` (Android APK)  

---

## 📜 License
This project is released under the **MIT License**. See [LICENSE](./LICENSE).
