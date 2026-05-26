// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AppleIntelligenceBridge",
    platforms: [
        // FoundationModels was introduced in macOS 15 (Sequoia).
        // Full Apple Intelligence (on-device LLM) requires macOS 26+.
        // The runtime aib_is_available() check gates actual usage.
        .macOS(.v15),
    ],
    products: [
        .library(
            name: "AppleIntelligenceBridge",
            type: .dynamic,
            targets: ["AppleIntelligenceBridge"]
        ),
    ],
    targets: [
        .target(
            name: "AppleIntelligenceBridge",
            path: "Sources/AppleIntelligenceBridge",
            swiftSettings: [
                .enableExperimentalFeature("StrictConcurrency"),
            ],
            linkerSettings: [
                .linkedFramework("FoundationModels"),
            ]
        ),
    ]
)
