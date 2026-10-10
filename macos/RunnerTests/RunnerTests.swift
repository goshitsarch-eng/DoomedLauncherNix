import Cocoa
import Darwin
import FlutterMacOS
import XCTest

class RunnerTests: XCTestCase {
  func testApplicationIdentityAndVersion() throws {
    XCTAssertEqual(Bundle.main.bundleIdentifier, "com.goshapps.DoomLauncher")
    XCTAssertEqual(Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String,
                   "Doom Launcher")
    let version = try XCTUnwrap(Bundle.main.object(forInfoDictionaryKey: "DoomLauncherVersion") as? String)
    XCTAssertNotNil(version.range(of: #"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?$"#,
                                 options: .regularExpression))
  }

  func testFinderDocumentRegistration() throws {
    let declarations = try XCTUnwrap(Bundle.main.object(forInfoDictionaryKey: "CFBundleDocumentTypes") as? [[String: Any]])
    let extensions = Set(declarations.flatMap { $0["CFBundleTypeExtensions"] as? [String] ?? [] })
    XCTAssertTrue(Set(["wad", "pk3", "deh", "bex"]).isSubset(of: extensions))
    XCTAssertTrue(declarations.allSatisfy { $0["LSHandlerRank"] as? String == "Alternate" })
  }

  func testEmbeddedRustAbi() throws {
    let frameworks = try XCTUnwrap(Bundle.main.privateFrameworksURL)
    let library = frameworks.appendingPathComponent("libdoomedlauncher_core.dylib")
    XCTAssertTrue(FileManager.default.fileExists(atPath: library.path))
    let handle = try XCTUnwrap(dlopen(library.path, RTLD_NOW | RTLD_LOCAL))
    defer { _ = dlclose(handle) }
    let versionSymbol = try XCTUnwrap(dlsym(handle, "dl_abi_version"))
    typealias AbiVersion = @convention(c) () -> UInt32
    let abiVersion = unsafeBitCast(versionSymbol, to: AbiVersion.self)
    XCTAssertEqual(abiVersion(), 1)
    XCTAssertNotNil(dlsym(handle, "dl_invoke"))
    XCTAssertNotNil(dlsym(handle, "dl_free"))
  }
}
