# Desktop and Web Integration Suite

This repository serves as an experimental playground for testing the capabilities of C# .NET on Windows. It contains a
collection of projects designed to explore Windows bindings, non-standard features, and unique integrations between web
interfaces and desktop applications. The primary goal is to push the boundaries and discover what is possible when
combining modern web technologies with low-level system interactions.

## Components

The solution is divided into several key projects, grouped by functionality:

### WV2 Hybrid Application Suite

- **`WV2/Server.API`**: The ASP.NET Core data backend that provides APIs for the entire system.
- **`WV2/Server.Client`**: The interactive user interface built with React. This web application is rendered within the
  desktop client.
- **`WV2/Windows.Client`**: The main C# desktop application that uses WebView2 (WV2) to host and render the
  `Server.Client` React interface.

### System Interaction & Utilities

- **`ClipboardGuardApp`**: A standalone C# Windows Forms application demonstrating clipboard monitoring functionality.
- **`USER`**: A C# project containing utilities for interacting with the user's desktop session, such as drawing text
  directly onto the desktop.
- **`WIN32`**: A C# project focused on low-level Windows operations, including interaction with Win32 APIs and UEFI
  settings.

## Key Features

- **Hybrid Desktop-Web Architecture**: The core of the suite is a hybrid application combining a C# .NET desktop
  client (`Windows.Client`) with a modern web frontend (`Server.Client`). The desktop app uses WebView2 to render the
  React-based user interface, which is served by an ASP.NET Core backend (`Server.API`). This architecture demonstrates
  a powerful pattern for building native-feeling applications with web technologies.

- **Advanced Clipboard Management (`ClipboardGuardApp`)**: A utility that showcases secure clipboard handling. It
  monitors clipboard activity and automatically clears any copied content when the application loses focus, preventing
  sensitive data from being accidentally exposed. The application can also identify the source process of pasted
  content.

- **Direct Desktop Interaction (`USER` project)**: This project explores drawing directly onto the Windows desktop
  canvas using GDI+ P/Invoke calls. It can render text, shapes, and images as a persistent overlay, offering a practical
  example of low-level graphics manipulation outside of a standard application window.
-
- **Low-Level System Access (`WIN32` project)**: Demonstrates interaction with core Windows internals through the Win32
  API. This includes a utility to enumerate and read UEFI (Unified Extensible Firmware Interface) environment variables,
  providing direct access to firmware-level system configuration from managed C# code.

## Core Dependencies

The suite is built on the following key technologies, categorized by function:

#### Backend: `Server.API`

- **.NET 9 & ASP.NET Core**: The foundation for the backend web services.
- **`Microsoft.AspNetCore.OpenApi`**: Enables OpenAPI (Swagger) support for API documentation and testing.

#### Frontend: `Server.Client`

- **React**: Powers the interactive user interface of the single-page application.
- **`react` & `react-dom`**: The core libraries for building the UI.
- **`react-scripts`**: Provides the standard build and development scripts.

#### Desktop & Hybrid Client: `Windows.Client`

- **Windows Forms**: The framework for the native desktop application.
- **`Microsoft.Web.WebView2`**: The core package for embedding the React web UI.
- **`Microsoft.Extensions.*`**: A suite of packages for dependency injection and configuration.
- **`Serilog`**: Provides structured, configurable logging for the desktop client.

#### System & Low-Level Utilities

- **`USER` Project**: Uses `System.Drawing.Common` for GDI+ drawing operations.
- **`WIN32` & `ClipboardGuardApp`**: Utilize **P/Invoke** to call native Windows APIs (like `user32.dll` and
  `gdi32.dll`) for direct OS interaction, without major external NuGet packages.

## Building and Running

### Prerequisites

- .NET 9 SDK (or newer)
- Node.js and npm
- **One** of the following development environments:
    - **Visual Studio 2022**: with "ASP.NET and web development" and ".NET desktop development" workloads.
    - **JetBrains Rider**: 2023.x or newer.
    - **Visual Studio Code**: with the C# Dev Kit extension installed.

### WV2 Hybrid Application Suite

This is the main hybrid application. To run it, you need to start the backend API, the React frontend, and the Windows
desktop client.

#### 1. Setup Frontend Dependencies

Navigate to the `WV2/Server.Client` directory in your terminal and install the required Node.js packages:

```bash
npm install
```

#### 2. Running the Full Application

You need to run three components simultaneously, each in its own terminal.

1. **Start the Backend API**:
   ```bash
   dotnet run --project WV2/Server.API/Server.API.csproj
   ```

2. **Start the React Frontend**:
   ```bash
   cd WV2/Server.Client
   npm start
   ```
   This starts the React development server, typically on `http://localhost:3000`.

3. **Run the Desktop Client**:
   ```bash
   dotnet run --project WV2/Windows.Client/Windows.Client.csproj
   ```
   The Windows Forms application will launch and load the React UI from the development server.

---

### System Interaction & Utilities

These are standalone projects for exploring specific Windows features. They can be built and run independently from the
command line.

#### ClipboardGuardApp

A Windows Forms application that monitors and clears the clipboard.

- **Build and Run**:
  ```bash
  dotnet run --project ClipboardGuardApp/ClipboardGuardApp.csproj
  ```

#### USER Project

An application that demonstrates drawing directly onto the desktop.

- **Build and Run**:
  ```bash
  dotnet run --project USER/USER.csproj
  ```

#### WIN32 Project

A console application for listing UEFI environment variables.

- **Build and Run**:
  ```bash
  dotnet run --project WIN32/WIN32.csproj
  ```
  _**Note**: This may require administrative privileges to function correctly._

---

> **Disclaimer**: This project is highly experimental and intended for educational and research purposes. It tests
> Windows bindings and non-standard features. Many components interact with the operating system at a low level (e.g.,
> clipboard monitoring, UEFI access). Use these features responsibly and only on systems you own and have permission to
> test on.
