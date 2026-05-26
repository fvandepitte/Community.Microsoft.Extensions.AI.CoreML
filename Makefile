# Build the Swift bridge dylib and copy it to the .NET runtime assets folder.
# Must be run on macOS arm64 with Xcode 26 beta 2+ installed.

BRIDGE_DIR   := native
OUTPUT_DIR   := src/Community.Microsoft.Extensions.AI.CoreML/runtimes/osx-arm64/native
DYLIB_NAME   := libAppleIntelligenceBridge.dylib
BUILD_OUTPUT := $(BRIDGE_DIR)/.build/release/$(DYLIB_NAME)

.PHONY: bridge clean

## bridge: compile the Swift dylib and copy it into the NuGet runtime assets path
bridge:
	@echo "→ Building Swift bridge (release)..."
	cd $(BRIDGE_DIR) && swift build -c release
	@mkdir -p $(OUTPUT_DIR)
	cp "$(BUILD_OUTPUT)" "$(OUTPUT_DIR)/$(DYLIB_NAME)"
	@echo "✓ $(OUTPUT_DIR)/$(DYLIB_NAME)"

## clean: remove build artefacts
clean:
	cd $(BRIDGE_DIR) && swift package clean
	rm -f "$(OUTPUT_DIR)/$(DYLIB_NAME)"
