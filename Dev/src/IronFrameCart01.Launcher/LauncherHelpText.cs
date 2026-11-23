// @module: IronFrameCart01.Launcher.LauncherHelpText
// @purpose: Clean-room CLI usage text and 3-part missing-engine remediation guidance with zero host paths or trademarks.
// @depends-on: System
// @exports: LauncherHelpText

namespace IronFrameCart01.Launcher;

/// <summary>
/// Clean-room launcher help text and actionable remediation messages.
/// </summary>
internal static class LauncherHelpText
{
    internal const string ProductVersion = "0.1.0";
    internal const string DisplayTitle = "IronFrame OuterFortress (Cart) 01";
    internal const string ModuleId = "ironframe-outerfortress-cart-01";
    internal const string DefaultRecompFileName = "IronFrame-OuterFortress-Cart-01.recomp";
    internal const string DefaultEngineTrack = "0.1.x";
    internal const string DefaultMinEngineVersion = "0.1.3";

    internal static string VersionLine => $"{DisplayTitle} v{ProductVersion} ({ModuleId})";

    internal const string Usage = """
        IronFrame OuterFortress (Cart) 01 v0.1.0 — Clean-Room Cartridge Launcher
        Usage: IronFrame-OuterFortress-Cart-01.exe [options]

        Suite & Engine Routing:
          --reengine-dir <dir>              Explicit reEngine installation directory override
          --suite-dir <dir>                 Explicit StreamItAll\reEngine suite umbrella directory
          --open <path>, --module <path>    Override .recomp package (default: app\IronFrame-OuterFortress-Cart-01.recomp)

        Execution & Media:
          --rom <path>, --preload-media     Supply or discover local BYO cartridge image under data\rom\
          --backend <id>                    opengl | vulkan | directx | parallelCpu | software
          --aspect-mode <mode>              nativePar | fourByThree | squareInteger | nativeFit | horPlus16By9
          --scale <1|2|4|8>                 Integer viewport scale multiplier (default: 2)
          --region <auto|ntsc|pal|dendy>    Target frame/scanline region standard
          --execution-tier <tier>           staticAotOnly | aotWithJitFallback | aotWithThreadedIr | jitBringup | interpreterOracle
          --lockstep-oracle                 Enable quarantined Tier 4 differential oracle verification
          --lockstep-steps <n>              Run N-step AOT vs reference oracle differential lockstep
          --fullscreen, --window            Start borderless fullscreen or desktop windowed
          --frames <n>                      Run fixed verification frame count and exit
          --dump-screenshot <png>           Write deterministic final composite frame PNG
          --dump-audio <wav>                Write deterministic stereo PCM WAV capture

        Environment & Diagnostics:
          --data-dir <dir>                  Override writable data, local AOT cache, and save root
          --config <path>                   Override core settings JSON file path
          --log-dir <dir>                   Override diagnostic log directory
          --save-dir <dir>                  Override save and quick-save directory
          --port <n>                        Override host service port
          --tray-only, --dry-run, --verify  Tray/background mode, preflight dry-run, or cartridge/runtime verify
          --wait, --enable-logs             Wait synchronously for player exit or enable verbose logs
          --version, --help                 Print launcher version or show this usage help
          (Set APP_ISOLATED_TEST=1 to enforce strict --data-dir isolation)
        """;

    /// <summary>
    /// Formats a clean-room 3-part remediation message when no compatible <c>reCart</c> runtime is found.
    /// </summary>
    internal static string FormatMissingEngineRemediation(
        string engineId,
        string track,
        string minVersion,
        string? detail = null)
    {
        string detailSuffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail.Trim()})";
        return string.Join(Environment.NewLine,
            $"What happened: No compatible {engineId} runtime (track '{track}' >= v{minVersion}) was found{detailSuffix}.",
            $"Why: {DisplayTitle} distributes only clean-room cartridge metadata and delegates execution to a compatible {engineId} player (reCart.exe / app\\reCart.Player.exe).",
            $"How to fix: Install reEngine + {engineId} v{minVersion}+ (using the Full installer or under %LOCALAPPDATA%\\Programs\\StreamItAll\\reEngine\\engines\\{engineId}\\v{minVersion}\\), or pass --reengine-dir <dir> or --suite-dir <dir>.");
    }
}
