# DLL Injector

A professional and secure DLL injection tool developed in C# with WPF that allows injecting dynamic link libraries (DLLs) into running processes on Windows systems. This tool is designed for legitimate use cases such as development, testing, modding, and debugging.

## 🚀 Features

- **Process Enumeration**: Lists all active processes on the system in alphabetical order
- **Safe DLL Injection**: Uses the classic LoadLibrary injection technique with proper error handling
- **Architecture Validation**: Automatically validates that DLL and process architectures match (32-bit vs 64-bit)
- **PE File Validation**: Verifies that selected files are valid Portable Executable (PE) DLLs
- **Resource Management**: Properly cleans up all Windows handles and resources
- **Modern UI**: Clean WPF interface with custom styling
- **Comprehensive Error Handling**: Detailed error messages and validation checks

## 🛠️ Requirements

- **Operating System**: Windows 7 or later
- **.NET Runtime**: .NET 6.0 or higher
- **Permissions**: Administrator privileges (required for process injection)
- **Development** (optional): Visual Studio 2022 or .NET SDK 6.0+

## 📦 Installation

### Pre-built Binary

1. Download the latest release from the [Releases](https://github.com/yeipills/injector/releases) page
2. Extract the archive to your desired location
3. Run `injector.exe` as Administrator

### Build from Source

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yeipills/injector.git
   cd injector
   ```

2. **Build the project**:
   ```bash
   dotnet build -c Release
   ```

3. **Run the application**:
   ```bash
   dotnet run --project injector
   ```

## 📖 Usage

1. **Launch the application** as Administrator (right-click → Run as administrator)

2. **Select a target process**:
   - The process list will populate automatically
   - Use the dropdown to select your target process

3. **Select a DLL file**:
   - Click "Select DLL" button
   - Browse and select the DLL file you want to inject
   - The application will validate that it's a valid PE DLL

4. **Inject the DLL**:
   - Click the "Inject" button
   - The application will:
     - Verify the process is still running
     - Check architecture compatibility (32-bit vs 64-bit)
     - Inject the DLL into the target process
     - Display success or detailed error messages

5. **Refresh process list**:
   - Click "Refresh" to update the process list at any time

## ⚙️ Technical Details

### Injection Method

This tool uses the classic **LoadLibrary injection** technique:

1. Opens the target process with `OpenProcess()`
2. Allocates memory in the remote process using `VirtualAllocEx()`
3. Writes the DLL path to allocated memory with `WriteProcessMemory()`
4. Creates a remote thread that calls `LoadLibraryA()` via `CreateRemoteThread()`

### Security Features

- **PE Validation**: Checks MZ and PE headers to ensure valid DLL files
- **Architecture Matching**: Prevents injection of 32-bit DLLs into 64-bit processes and vice versa
- **Resource Cleanup**: All handles are properly released using try-finally blocks
- **Admin Check**: Requires administrator privileges for security transparency

## ⚠️ Legal Disclaimer

This tool is provided for **educational and legitimate purposes only**, including:

- Software development and debugging
- Creating and testing mods for single-player games
- Security research and penetration testing (with proper authorization)
- Reverse engineering for interoperability

**Users are responsible for ensuring their use complies with:**
- Applicable laws and regulations
- Terms of Service of applications being modified
- Software licenses and End User License Agreements (EULAs)

**The authors are not responsible for misuse of this tool.**

## 🤝 Contributing

Contributions are welcome! If you'd like to contribute:

1. Fork the project
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📜 License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE.txt) file for details.

## 🔒 Security

- Always verify the source and integrity of DLL files before injection
- Be cautious when injecting into system processes
- Use in isolated/test environments when possible
- Keep your system and antivirus software updated
