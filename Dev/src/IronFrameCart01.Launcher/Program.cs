// @module: IronFrameCart01.Launcher.Program
// @purpose: Clean-root <2 MB WinExe launcher with embedded AssemblyResolve, Release ExitCode=2 gate, containment, and reCart launch.
// @depends-on: System, System.Collections.Generic, System.ComponentModel, System.Diagnostics, System.IO, System.Reflection, System.Runtime.CompilerServices, System.Runtime.InteropServices, Foundation.Core
// @exports: Program

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation.Core;

namespace IronFrameCart01.Launcher;

internal static class Program
{
    public const bool AllowAutoStart = false;
    private const string EmbeddedAssembliesPrefix = "IronFrameCart01.Launcher.Assemblies.";
    private const int AttachParentProcess = -1;
    private static readonly object ResolveLock = new();
    private static readonly Dictionary<string, Assembly> LoadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EmbeddedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Foundation.Core",
        "Foundation.Launcher",
        "Foundation.Retro",
        "reEngine.Core",
        "reCart.Contracts"
    };

    internal static Func<ProcessStartInfo, int>? LaunchSeamForTests { get; set; }

    static Program() => AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        try { return RunLauncher(args ?? Array.Empty<string>()); }
        finally { Console.Out.Flush(); Console.Error.Flush(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RunLauncher(string[] args) =>
        Run(args, baseDirOverride: null, callerWorkingDirOverride: null);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string[] args, string? baseDirOverride = null, string? callerWorkingDirOverride = null)
    {
        args ??= Array.Empty<string>();
        if (args.Length > 0) AttachParentConsoleIfInteractive();

#if !IRONFRAME_TEST_AUTOMATION
        if (LauncherCliAndContainment.HasAutomationPortFlag(args))
        {
            Console.Error.WriteLine("Automation loopback (--automation-port) is disabled in Release builds (ExitCode=2).");
            return 2;
        }
#endif

        LauncherCliOptions options;
        try { options = LauncherCliAndContainment.ParseAndNormalize(args, callerWorkingDirOverride ?? Environment.CurrentDirectory); }
        catch (ArgumentException ex) { Console.Error.WriteLine($"Launcher argument error: {ex.Message}"); return 2; }

        if (options.ShowHelp) { Console.WriteLine(LauncherHelpText.Usage); return 0; }
        if (options.ShowVersion) { Console.WriteLine(LauncherHelpText.VersionLine); return 0; }

        string baseDir = string.IsNullOrWhiteSpace(baseDirOverride) ? AppContext.BaseDirectory : Path.GetFullPath(baseDirOverride);
        AppEnvironment env;
        string saveDir;
        try
        {
            env = new AppEnvironment(LauncherHelpText.ModuleId, options.SanitizedArgs, baseDir, Path.Combine("config", "settings.json"));
            saveDir = string.IsNullOrWhiteSpace(options.SaveDir)
                ? Path.Combine(env.DataDirectory, "saves")
                : Path.GetFullPath(options.SaveDir);
            LauncherCliAndContainment.ValidateContainment(options, env, saveDir);
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }

        try
        {
            CrashBundleService.RegisterGlobalHandlers(env.LogDirectory, LauncherHelpText.ModuleId, LauncherHelpText.ProductVersion);

            string? openPath = ResolveEffectiveOpenPath(options, baseDir);
            if (!options.DryRun && !options.Verify)
                LauncherCliAndContainment.SeedDefaultSettingsIfMissing(env.DataDirectory, env.ConfigFilePath);

            EngineSearchResult engine = EngineResolver.ResolvePlayerExecutable(baseDir, options, env, openPath, Environment.ProcessPath);
            if (engine.PlayerExecutablePath is null)
            {
                ReportMissingEngine(engine.DiagnosticMessage, options, env);
                return 1;
            }

            string[] forwardedArgs = BuildPlayerArguments(options, env, saveDir, openPath);
            forwardedArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(
                options, forwardedArgs, env, baseDir, openPath, engine.PlayerExecutablePath);
            return LaunchResolvedPlayer(engine.PlayerExecutablePath, forwardedArgs, options.WaitRequested);
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
        catch (Exception ex)
        {
            CrashBundleService.TryWriteCrashBundle(env.LogDirectory, LauncherHelpText.ModuleId, LauncherHelpText.ProductVersion, ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static string? ResolveEffectiveOpenPath(LauncherCliOptions options, string baseDir)
    {
        if (options.HasExplicitOpenOrModule) return options.OpenPath;
        if (options.DryRun) return null;
        return Path.Combine(Path.GetFullPath(baseDir), "app", LauncherHelpText.DefaultRecompFileName);
    }

    internal static string[] BuildPlayerArguments(LauncherCliOptions options, AppEnvironment env, string saveDir, string? openPath)
    {
        var args = new List<string>(options.ForwardedArgs.Length + 10);
        if (!string.IsNullOrWhiteSpace(openPath))
        {
            args.Add(options.UsedLegacyModuleFlag ? "--module" : "--open");
            args.Add(openPath);
        }
        args.Add("--data-dir"); args.Add(env.DataDirectory);
        args.Add("--config"); args.Add(env.ConfigFilePath);
        args.Add("--log-dir"); args.Add(env.LogDirectory);
        args.Add("--save-dir"); args.Add(saveDir);
        foreach (string item in options.ForwardedArgs) args.Add(item);
        return args.ToArray();
    }

    private static int LaunchResolvedPlayer(string playerExe, string[] forwardedArgs, bool waitForChild)
    {
        string? selfPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(selfPath) &&
            string.Equals(Path.GetFullPath(playerExe), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Fail-closed self-spawn guard: refusing to launch '{playerExe}' from itself.");
            return 1;
        }

        bool redirOut = waitForChild && Console.IsOutputRedirected;
        bool redirErr = waitForChild && Console.IsErrorRedirected;
        var psi = new ProcessStartInfo
        {
            FileName = playerExe,
            WorkingDirectory = Path.GetDirectoryName(playerExe) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = waitForChild && !redirOut && !redirErr,
            RedirectStandardOutput = redirOut,
            RedirectStandardError = redirErr
        };
        foreach (string arg in forwardedArgs) psi.ArgumentList.Add(arg);

        if (LaunchSeamForTests is { } seam) return seam(psi);

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            Console.Error.WriteLine($"Failed to start reCart player at '{playerExe}'.");
            return 1;
        }

        if (!waitForChild) return 0;

        if (redirOut) proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.Out.WriteLine(e.Data); };
        if (redirErr) proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        if (redirOut) proc.BeginOutputReadLine();
        if (redirErr) proc.BeginErrorReadLine();

        int cancelled = 0;
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; Interlocked.Exchange(ref cancelled, 1); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            while (!proc.WaitForExit(100))
            {
                if (Volatile.Read(ref cancelled) != 1) continue;
                if (!proc.WaitForExit(4000) && !proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
                }
                break;
            }
            if (Volatile.Read(ref cancelled) == 0) proc.WaitForExit();
            return Volatile.Read(ref cancelled) == 1 ? 130 : proc.ExitCode;
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    private static void ReportMissingEngine(string message, LauncherCliOptions options, AppEnvironment env)
    {
        Console.Error.WriteLine(message);
        bool interactiveDesktop = !options.WaitRequested && !LauncherCliAndContainment.IsIsolatedTestEnabled() &&
                                  !env.Sandbox.IsSandboxActive && Environment.UserInteractive &&
                                  !Console.IsOutputRedirected && !Console.IsErrorRedirected && OperatingSystem.IsWindows();
        if (interactiveDesktop)
            _ = MessageBoxW(IntPtr.Zero, message, $"{LauncherHelpText.DisplayTitle} v{LauncherHelpText.ProductVersion} — Missing reCart Engine", 0x00000030);
    }

    private static void AttachParentConsoleIfInteractive()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
            _ = AttachConsole(AttachParentProcess);
    }

    private static Assembly? ResolveEmbeddedAssembly(object? sender, ResolveEventArgs args)
    {
        string? simpleName = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(simpleName) || !EmbeddedAssemblyNames.Contains(simpleName)) return null;

        lock (ResolveLock)
        {
            if (LoadedAssemblies.TryGetValue(simpleName, out Assembly? cached)) return cached;
            foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(existing.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                {
                    LoadedAssemblies[simpleName] = existing;
                    return existing;
                }
            }

            string resourceName = $"{EmbeddedAssembliesPrefix}{simpleName}.dll";
            using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            byte[] raw = new byte[stream.Length];
            stream.ReadExactly(raw);
            Assembly loaded = Assembly.Load(raw);
            LoadedAssemblies[simpleName] = loaded;
            return loaded;
        }
    }
}
