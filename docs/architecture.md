# 🏗️ Architecture Overview — MobControl

MobControl transforms an Android smartphone into a dynamic game controller for PC games.  
The system consists of two main components:

- **📱 Mobile App** — Unity 6000.0.37f1 (Android)  
- **🖥️ Desktop App** — WPF, .NET 8 (Windows)  

They communicate securely over a local network using **WebSocket (TLS)** and a **session token/QR pairing system**.

---

## 🔄 High-Level Architecture

```mermaid
flowchart LR
    U[👤 User] --> M[📱 Mobile App (Unity)]
    M -->|QR Scan / Token| D[🖥️ Desktop App (WPF/.NET)]
    D -->|WebSocket (TLS)| G[🎮 Game / Application]
```

---

## 📱 Mobile App (Unity)

- Displays a customizable controller UI (buttons, d-pad, multi-touch).  
- Supports motion/accelerometer input for future use.  
- Handles **pairing** by scanning the QR code or manually entering a token.  
- Sends mapped input events to the Desktop App over **WebSocket (TLS, port 8181)**.  

---

## 🖥️ Desktop App (WPF / .NET 8)

- Hosts a **WebSocket server** to accept mobile connections.  
- Generates and displays a **QR code** with a session token for pairing.  
- Manages multiple player connections, each with an isolated input stream.  
- Translates incoming inputs into desktop actions (keyboard/mouse/XInput mapping).  
- Loads and updates **JSON layouts** for flexible controller designs.  

---

## 🔐 Security Model

- **Pairing Process**  
  - Desktop generates a unique session token.  
  - Token is embedded in a QR code.  
  - Mobile scans the code to establish a trusted connection.  

- **Transport Security**  
  - WebSocket communication secured with **TLS**.  
  - Authentication using **pre-shared keys** during session setup.  

- **Session Isolation**  
  - Each connected mobile has its own session.  
  - Prevents input overlap between players.  

---

## 🧩 Data Flow

1. **Session Setup**  
   - Desktop app launches server → generates token → shows QR.  
   - Mobile app scans QR → connects via WebSocket.  

2. **Input Mapping**  
   - User taps buttons or gestures on Mobile.  
   - Events serialized to JSON → sent over WebSocket.  
   - Desktop parses → maps to keyboard/mouse/XInput events.  

3. **Game Interaction**  
   - Desktop app injects inputs.  
   - PC game/app receives them as if from a physical controller.  

---

## 📚 References

- 📖 [Setup Guide](./setup-guide.md)  
- 📄 [User Manual](./User%20Manual.pdf)  
