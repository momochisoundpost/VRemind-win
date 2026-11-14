// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "VRemindCoreCLI",
    platforms: [
        .macOS(.v12)
    ],
    products: [
        .library(
            name: "VRemindCoreCLIKit",
            targets: ["VRemindCoreCLIKit"]
        ),
        .executable(
            name: "vremind-core",
            targets: ["VRemindCoreCLI"]
        ),
    ],
    dependencies: [
        .package(path: "../VRemindCore")
    ],
    targets: [
        .target(
            name: "VRemindCoreCLIKit",
            dependencies: [
                .product(name: "VRemindCore", package: "VRemindCore")
            ]
        ),
        .executableTarget(
            name: "VRemindCoreCLI",
            dependencies: ["VRemindCoreCLIKit"]
        ),
        .testTarget(
            name: "VRemindCoreCLITests",
            dependencies: ["VRemindCoreCLIKit"]
        ),
    ]
)
