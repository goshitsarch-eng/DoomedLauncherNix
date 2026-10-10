import Cocoa
import FlutterMacOS

class AppDelegate: FlutterAppDelegate {
  private var pendingFiles: [String] = []
  private var lifecycleChannel: FlutterMethodChannel?
  private var dartReady = false
  private var shutdownReady = false
  private var pendingTermination: UUID?
  private var terminationTimeout: DispatchWorkItem?

  func configureLifecycle(engine: FlutterEngine) {
    let channel = FlutterMethodChannel(name: "doomedlauncher/lifecycle", binaryMessenger: engine.binaryMessenger)
    lifecycleChannel = channel
    channel.setMethodCallHandler { [weak self] call, result in
      guard let self = self else { result([]); return }
      if call.method == "ready" {
        self.dartReady = true
        let files = self.pendingFiles
        self.pendingFiles.removeAll()
        result(files)
      } else if call.method == "shutdownReady" {
        if let arguments = call.arguments as? [String: Any],
           let requestID = arguments["requestId"] as? String {
          guard self.pendingTermination?.uuidString == requestID else {
            result(false)
            return
          }
          self.terminationTimeout?.cancel()
          self.terminationTimeout = nil
        }
        self.shutdownReady = true
        result(true)
      } else if call.method == "shutdownCancelled" {
        self.shutdownReady = false
        result(nil)
      } else {
        result(FlutterMethodNotImplemented)
      }
    }
  }

  override func applicationWillFinishLaunching(_ notification: Notification) {
    super.applicationWillFinishLaunching(notification)
    if let controller = mainFlutterWindow?.contentViewController as? FlutterViewController {
      configureLifecycle(engine: controller.engine)
    }
  }

  override func application(_ application: NSApplication, open urls: [URL]) {
    let filenames = urls.filter { $0.isFileURL }.map { $0.path }
    let otherURLs = urls.filter { !$0.isFileURL }
    if !otherURLs.isEmpty { super.application(application, open: otherURLs) }
    guard !filenames.isEmpty else { return }
    if dartReady, let channel = lifecycleChannel {
      channel.invokeMethod("openFiles", arguments: filenames)
    } else {
      pendingFiles.append(contentsOf: filenames)
    }
  }

  override func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
    return true
  }

  override func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
    if shutdownReady { return .terminateNow }
    guard dartReady, let channel = lifecycleChannel else { return .terminateNow }
    if pendingTermination != nil { return .terminateLater }

    let request = UUID()
    pendingTermination = request
    let timeout = DispatchWorkItem { [weak self] in
      guard let self = self, self.pendingTermination == request, !self.shutdownReady else { return }
      self.pendingTermination = nil
      self.terminationTimeout = nil
      // A stalled engine must not bypass the user's active-session safeguard.
      sender.reply(toApplicationShouldTerminate: false)
    }
    terminationTimeout = timeout
    DispatchQueue.main.asyncAfter(deadline: .now() + 60, execute: timeout)
    channel.invokeMethod("requestQuit", arguments: ["requestId": request.uuidString]) { [weak self] result in
      DispatchQueue.main.async {
        guard let self = self, self.pendingTermination == request else { return }
        self.terminationTimeout?.cancel()
        self.terminationTimeout = nil
        self.pendingTermination = nil
        let approved = result as? Bool ?? false
        self.shutdownReady = approved
        sender.reply(toApplicationShouldTerminate: approved)
      }
    }
    return .terminateLater
  }

  override func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
    return true
  }
}
