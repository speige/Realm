# 🌐 Realm Seed Node Setup Guide

This guide describes how to host a community **Lobby & Discovery Server** (also known as a **Seed Node**). 

---

### ⚠️ IMPORTANT DISCLAIMER
* **For Advanced Users Only:** Setting up and maintaining a seed node requires basic knowledge of networking, command-line interfaces, and router port-forwarding.
* **No Pressure:** The Realm network is decentralized and only requires a small handful of stable, high-uptime seed nodes globally to support the entire player base. You do not need to host one unless you want to support the community.
* **Uptime & Network Requirements:** To be accepted as a public seed node, your server **must maintain high uptime** and have a **stable internet connection**.
* **Static IP Required:** Public seed nodes **must have a static IP address** or a persistent dynamic DNS domain name. Dynamic/frequently-changing IPs will not be accepted.
* **Security Notice:** Running a seed node exposes port `5000` to the internet. While the server does not execute arbitrary code, hosting any public-facing server carries default network security considerations.

---

## 🧠 What is a Seed Node?
When a player hosts a game, the client contacts a registry server configured in [servers.json](file:///C:/temp/Realm/Realm.Godot/servers.json). The registry server:
1. Records the host's public IP, port, and NAT type.
2. Relays connection requests and coordinates UDP hole punching via WebSockets to enable direct peer-to-peer gameplay.

The server logic is implemented in [Program.cs](file:///C:/temp/Realm/Realm.Lobby/Program.cs).

---

## 🛠️ Prerequisites
* **Runtime:** [.NET 10.0 SDK or Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
* **Port Forwarding:** Access to your router settings to forward port **5000 TCP** to your hosting machine.
* **Network:** A stable internet connection with a static IP address.

---

## 🚀 How to Run the Seed Node

### Option A: From Source Code (For Developers & Advanced Users)
1. Clone the repository and navigate to the project directory.
2. Run the lobby server project with the `--urls` flag to listen on all network interfaces:
   ```powershell
   dotnet run --project Realm.Lobby/Realm.Lobby.csproj --urls "http://0.0.0.0:5000"
   ```
   *File Reference: [Realm.Lobby.csproj](file:///C:/temp/Realm/Realm.Lobby/Realm.Lobby.csproj)*

### Option B: Using Pre-compiled Executables (User-Friendly Setup)
1. Download the latest `Realm.Lobby` release build for your operating system.
2. Open a terminal (cmd or terminal app) in the folder where the files were extracted.
3. Run the executable:
   * **Windows:**
     ```powershell
     .\Realm.Lobby.exe --urls "http://0.0.0.0:5000"
     ```
   * **Linux/macOS:**
     ```bash
     chmod +x ./Realm.Lobby
     ./Realm.Lobby --urls "http://0.0.0.0:5000"
     ```

---

## 🔒 Router Configuration (Port Forwarding)
To allow players to reach your seed node:
1. Find your hosting computer's local IP address (e.g. `192.168.1.100`) via `ipconfig` (Windows) or `ifconfig`/`ip a` (Linux).
2. Access your router's administration panel (usually at `192.168.1.1` or `192.168.0.1`).
3. Set up a port forwarding rule:
   * **Service Name:** Realm Seed Node
   * **Protocol:** TCP
   * **External/Internal Port:** 5000
   * **Internal IP:** Your computer's local IP address.
4. Verify accessibility using external tools (like [CanYouSeeMe.org](https://canyouseeme.org/) on port 5000).

## 🌐 Registering Your Seed Node in the Network

Once your seed node is running and port forwarding is confirmed working:

### 1. Find Your Public IP Address
Your public IP address is needed for players to connect. You can retrieve it using one of the following methods:
* **Via Web Browser:** Visit [icanhazip.com](https://icanhazip.com/) or [ifconfig.me](https://ifconfig.me/).
* **Via PowerShell (Windows):**
  ```powershell
  (Invoke-WebRequest -Uri "https://icanhazip.com").Content.Trim()
  ```
* **Via Terminal (Linux/macOS):**
  ```bash
  curl icanhazip.com
  ```

### 2. Submit a Pull Request
To make your seed node available to all players:
1. Navigate to `Realm.Godot/servers.json` in the project's repository on GitHub.
2. Click the **Edit this file** (pencil icon) in the top-right corner. GitHub will handle forking the repository automatically.
3. Add your public IP or custom static domain to the `registryServers` array. For example:
   ```json
   {
     "registryServers": [
       "http://127.0.0.1:5000",
       "http://<YOUR_STATIC_PUBLIC_IP>:5000"
     ]
   }
   ```
4. Click **Propose changes** / **Commit changes**, select **Create a new branch and start a pull request**, and submit the PR.
5. Once verified for stability and uptime, your seed node will be merged and automatically discovered by game clients.


---

## 📈 Maintaining High Uptime

If you want your node to be a reliable bootstrap server for the community, consider the following uptime best practices:

### 1. Run as a Background Service
To prevent the application from closing when you close the terminal window:
* **Windows (using NSSM):**
  Use the [Non-Sucking Service Manager (NSSM)](https://nssm.cc/) to install it as a Windows service:
  ```powershell
  nssm install RealmLobbyService "C:\path\to\Realm.Lobby.exe" "--urls http://0.0.0.0:5000"
  nssm start RealmLobbyService
  ```
* **Linux (using systemd):**
  Create a systemd unit file at `/etc/systemd/system/realm-lobby.service`:
  ```ini
  [Unit]
  Description=Realm Lobby Discovery Server
  After=network.target

  [Service]
  ExecStart=/usr/bin/dotnet /path/to/Realm.Lobby.dll --urls "http://0.0.0.0:5000"
  WorkingDirectory=/path/to/
  Restart=always
  RestartSec=10
  SyslogIdentifier=realm-lobby
  User=nobody

  [Install]
  WantedBy=multi-user.target
  ```
  Enable and start it:
  ```bash
  sudo systemctl enable realm-lobby.service
  sudo systemctl start realm-lobby.service
  ```

### 2. Configure Auto-Restart on Crash
Operating systems or unexpected network interruptions can occasionally crash applications. Running the node via systemd (`Restart=always`) or an process manager like **PM2** ensures the server immediately spins back up.

