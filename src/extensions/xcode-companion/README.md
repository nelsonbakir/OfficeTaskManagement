# TaskFlow Xcode Companion App

This folder contains the complete native Swift source code required to build the native macOS Menu Bar Companion App for TaskFlow, filling the gap left by Xcode's strict extension limitations.

As this code was generated on a Windows machine, the actual `.xcodeproj` project wrapper could not be natively constructed. Follow these instructions on your **Mac** to compile and run the companion app.

### Prerequisites
- A Mac running **macOS 14+**
- **Xcode 15+** installed

### Setup Instructions

1. **Pull the Repository to your Mac**
   Clone or pull this `OfficeTaskManagement` repository directly onto your Mac.
   
2. **Create a new Xcode Project**
   - Open Xcode and select **Create a new Xcode project**.
   - Under the **macOS** tab, select **App** and click Next.
   - **Product Name:** `TaskFlowCompanion`
   - **Interface:** SwiftUI
   - **Language:** Swift
   - Click Next and save it anywhere on your Mac.

3. **Configure as a Menu Bar App (Hide Dock Icon)**
   Because this is a menu bar utility, we need to hide the standard dock icon.
   - Click your project root in the left Navigator to open Project Settings.
   - Go to the **Info** tab.
   - Look for "Application is agent (UIElement)". If it's not there, hover over a row, click the `+` button, type `Application is agent (UIElement)`, and set its value to **YES**.

4. **Inject the Source Code**
   Drag and drop the Swift files from this `xcode-companion` folder directly into your new Xcode project's navigator on the left to copy them natively into the project:
   - `App.swift` (When dragging this in, delete the default `TaskFlowCompanionApp.swift` file that Xcode generated to avoid entry point conflicts).
   - `Models.swift`
   - `NetworkManager.swift`
   - `KeychainHelper.swift`
   - `ContentView.swift`
   - `TaskDetailView.swift`

5. **Build and Run**
   Press **Cmd + R** (or click the Play button) to build your new macOS native app! Look for the checklist icon appearing instantly in your Mac's top status menu rail.

### Security Note
The Token authorization flow leverages Apple's `Keychain` native security hardware mapping through `KeychainHelper.swift`. By default, sandboxed Xcode apps allow this perfectly locally. If you ever deploy this app to the Mac App Store, you'll need to enable "Keychain Sharing" entitlements.
