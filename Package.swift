// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ThermalKun",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "ThermalKun", targets: ["ThermalKun"])],
    targets: [
        .target(name: "SMCBridge", publicHeadersPath: "include"),
        .executableTarget(name: "ThermalKun", dependencies: ["SMCBridge"], linkerSettings: [
            .linkedFramework("AppKit"), .linkedFramework("SwiftUI"),
            .linkedFramework("IOKit"), .linkedFramework("ServiceManagement")
        ])
    ]
)
