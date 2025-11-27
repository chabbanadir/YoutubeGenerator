using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace YoutubeGenerator.Utils
{
    public static class ProcessRunner
    {
        // Retourne un tuple (exitCode, stdout, stderr)
        // workingDirectory est optionnel (null -> current directory)
        public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            using var proc = new Process { StartInfo = psi };

            if (!proc.Start())
                throw new Exception($"Failed to start process: {fileName} {arguments}");

            var stdOutTask = proc.StandardOutput.ReadToEndAsync();
            var stdErrTask = proc.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdOutTask, stdErrTask);
            await proc.WaitForExitAsync(ct);

            return (proc.ExitCode, stdOutTask.Result, stdErrTask.Result);
        }
    }
}
