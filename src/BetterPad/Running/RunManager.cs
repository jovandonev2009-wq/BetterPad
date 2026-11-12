using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BetterPad.Running;

/// <summary>
/// Runs the current file with tools that are already installed on this machine.
/// No cloud compiler, no remote service.
/// </summary>
public sealed class RunManager : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;

    public event Action<string>? OutputReceived;

    public event Action<int>? ProcessExited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public string? Start(string filePath, bool build)
    {
        Stop();

        RunSpec spec = CreateSpec(filePath, build);
        if (spec.Error != null)
        {
            return spec.Error;
        }

        var startInfo = new ProcessStartInfo(spec.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = spec.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OutputReceived?.Invoke(e.Data + "\r\n");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OutputReceived?.Invoke(e.Data + "\r\n");
            }
        };
        process.Exited += (_, _) =>
        {
            int code = -1;
            try
            {
                code = process.ExitCode;
            }
            catch
            {
                // The process may already be gone.
            }

            OutputReceived?.Invoke($"\r\n[exited with code {code}]\r\n");
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            ProcessExited?.Invoke(code);
            process.Dispose();
        };

        lock (_gate)
        {
            _process = process;
        }

        try
        {
            if (!process.Start())
            {
                lock (_gate)
                {
                    _process = null;
                }

                process.Dispose();
                return "The process could not be started.";
            }
        }
        catch (Win32Exception ex)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            process.Dispose();
            return $"Could not start \"{spec.Executable}\". Is it installed and on PATH?\n\n{ex.Message}";
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            process.Dispose();
            return ex.Message;
        }

        OutputReceived?.Invoke($"> {spec.DisplayText}\r\n\r\n");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return null;
    }

    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
        }

        if (process == null)
        {
            return;
        }

        try
        {
            OutputReceived?.Invoke("\r\n[stopping process tree]\r\n");
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between the check and the kill.
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static RunSpec CreateSpec(string filePath, bool build)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string directory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;

        if (build)
        {
            string? project = extension is ".csproj" or ".sln" ? filePath : FindProject(directory);
            if (project == null)
            {
                return RunSpec.Fail("Build works on a .csproj or .sln file (or a .cs file inside a project folder).");
            }

            return new RunSpec("dotnet", ["build", project], directory,
                $"dotnet build \"{Path.GetFileName(project)}\"");
        }

        switch (extension)
        {
            case ".py":
                return new RunSpec("python", [filePath], directory, $"python \"{Path.GetFileName(filePath)}\"");
            case ".js":
            case ".mjs":
            case ".cjs":
                return new RunSpec("node", [filePath], directory, $"node \"{Path.GetFileName(filePath)}\"");
            case ".ps1":
                return new RunSpec("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", filePath], directory,
                    $"powershell -File \"{Path.GetFileName(filePath)}\"");
            case ".bat":
            case ".cmd":
                return new RunSpec("cmd.exe", ["/c", filePath], directory, $"cmd /c \"{Path.GetFileName(filePath)}\"");
            case ".csproj":
                return new RunSpec("dotnet", ["run", "--project", filePath], directory,
                    $"dotnet run --project \"{Path.GetFileName(filePath)}\"");
            case ".sln":
                return RunSpec.Fail("A solution cannot be run directly. Use Build, or run a project file.");
            case ".cs":
            {
                string? project = FindProject(directory);
                if (project == null)
                {
                    return RunSpec.Fail("This .cs file is not inside a project folder. Create a .csproj or use a project folder.");
                }

                return new RunSpec("dotnet", ["run", "--project", project], directory,
                    $"dotnet run --project \"{Path.GetFileName(project)}\"");
            }
            default:
                return RunSpec.Fail(
                    $"No local runner is configured for \"{extension}\".\n\nSupported: .py, .js, .mjs, .ps1, .bat, .cmd, .csproj, and .cs inside a project.");
        }
    }

    private static string? FindProject(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.csproj").OrderBy(path => path).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private sealed class RunSpec
    {
        public RunSpec(string executable, List<string> arguments, string workingDirectory, string displayText)
        {
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            DisplayText = displayText;
        }

        public string Executable { get; }

        public List<string> Arguments { get; }

        public string WorkingDirectory { get; }

        public string DisplayText { get; }

        public string? Error { get; private set; }

        public static RunSpec Fail(string error) => new(string.Empty, [], string.Empty, string.Empty) { Error = error };
    }
}
