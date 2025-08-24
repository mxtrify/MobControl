# 🚀 Setup Guide — MobControl

This guide explains how to set up and run **MobControl** on both Desktop and Mobile.

---

## 📋 1. Requirements

### 🖥️ Desktop
- Windows 10 or later  
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)  
- Visual Studio 2022 or `dotnet CLI`  
- Local network connection (Wi-Fi or LAN)  

### 📱 Mobile
- Android device (Android 10 or later)  
- [Unity Hub](https://unity.com/download)  
- Unity Editor **6000.0.37f1**  
- USB cable or wireless debugging enabled for APK deployment  
- Camera access (for QR code scanning)  

---

## 📂 2. Clone the Repository

```bash
git clone https://github.com/<your-username>/mobcontrol.git
cd mobcontrol
```

---

## 🖥️ 3. Desktop App Setup

1. Open the solution in Visual Studio 2022  
   **OR** navigate to `desktop-app/src/` and run:

   ```bash
   dotnet restore
   dotnet build -c Release
   ```

2. Start the application:

   ```bash
   dotnet run -c Release
   ```

3. The Desktop App will launch a WebSocket server on port **8181**.  
4. Ensure your firewall allows inbound connections on port `8181`.  

---

## 📱 4. Mobile App Setup

1. Open `mobile-app/` in Unity Hub using **Unity 6000.0.37f1**.  
2. In Unity, build the project for Android:  
   - Go to **File → Build Settings**  
   - Select **Android**  
   - Click **Build** to generate the APK  
3. Install the APK on your Android device.  
4. Make sure the mobile device is connected to the **same network** as the Desktop.  

---

## 🔗 5. Pairing

1. On the Desktop App, display the **pairing QR code**.  
2. On the Mobile App, open the **Pairing Page**.  
3. Scan the QR code or manually enter the session token.  
4. Once paired, the controller page will activate, and inputs will be sent to the Desktop.  

---

## 🎮 6. Testing

- Open a text editor or a supported game on the Desktop.  
- Press buttons on the Mobile App controller.  
- Inputs should be registered on the Desktop.  

---

## 🛠️ 7. Troubleshooting

- **Mobile cannot connect** → Ensure both devices are on the same LAN and port 8181 is not blocked by the firewall.  
- **QR code not scanning** → Use manual token entry.  
- **Inputs not registering in game** → Confirm mapping JSON is loaded correctly in the Desktop App.  

---

## 📚 8. References

- 📄 [User Manual](./user-manual.pdf)  
- 🗂️ [Architecture Overview](./architecture.md)  
