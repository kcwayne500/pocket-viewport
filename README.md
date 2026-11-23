# Pocket Viewport

Pocket Viewport is a lightweight Windows app for previewing websites in a floating, phone-sized window. It emulates the viewport, pixel ratio, touch input, and browser identity of Galaxy and iPhone presets.

## Build and run

On Windows x64, install a .NET SDK that can target .NET 8 and the Microsoft Edge WebView2 Runtime. From the repository root, run the following command.

```powershell
dotnet publish -c Release -o dist
```

Open `dist\PocketViewport.exe`. The single executable requires the .NET 8 Desktop Runtime (x64) and WebView2. To bundle the .NET runtime in the executable, publish with the following command. WebView2 remains a system dependency.

```powershell
dotnet publish -c Release --self-contained true -p:EnableCompressionInSingleFile=true -o dist
```

## Use Pocket Viewport

- Enter an HTTP or HTTPS URL and press **Enter**. Local addresses such as `localhost:3000` open over HTTP.
- Select **Device** in the app menu to switch between Galaxy (384 × 854) and iPhone (390 × 844). Switching devices reloads the page.
- **Ctrl+L** or the bottom center bar opens the URL field. **Esc** returns to the page if one is open. Select a recent website to reopen it.
- **Ctrl+Shift+M**, right-click, or the bottom dots open the app menu. Drag the top of the frame to move the window.
- The top bar displays the Windows clock, connection, and power status. Select the bell to read preview alerts.

Both presets use the Chromium-based WebView2 engine. Use Safari on an iPhone to verify Safari-specific behavior.

Pocket Viewport keeps its browser profile and the five most recent addresses on your computer. The default storage folder is `%LOCALAPPDATA%\PocketViewport`.
