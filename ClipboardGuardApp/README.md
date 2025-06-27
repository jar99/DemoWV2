# ClipboardGuardApp

ClipboardGuardApp is a Windows Forms demo application showcasing the features and capabilities of ClipboardGuard.

## Features
- Monitors and protects clipboard content in real time
- Displays current clipboard status and build information
- Shows release type and build metadata in non-release builds
- Modern, simple user interface
- Distributed as a self-contained Windows executable

## Versioning
- Follows [Semantic Versioning 2.0.0](https://semver.org/)
- Build and release metadata are embedded at build time
- See the About dialog or logs for version, build date, and commit info

## Building & Running
1. **Requirements:** .NET 9.0 SDK or later, Windows
2. **Build:**
   ```sh
   dotnet build ClipboardGuardApp/ClipboardGuardApp.csproj
   ```
3. **Run:**
   ```sh
   dotnet run --project ClipboardGuardApp/ClipboardGuardApp.csproj
   ```

## CI/CD & Release
- GitHub Actions workflow builds, versions, and publishes the app
- Build date, commit, and release type are set automatically
- See [RELEASENOTES](./RELEASENOTES) for changes

## License
MIT License. See [LICENSE](../LICENSE).
