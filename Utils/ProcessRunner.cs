using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeGenerator.Utils
{
    public static class ProcessRunner
    {
        public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            CancellationToken ct = default)
        {
            return RunStreamingAsync(fileName, arguments, workingDirectory, null, null, null, ct);
        }

        public static async Task<(int ExitCode, string StdOut, string StdErr)> RunStreamingAsync(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            Action<string>? onLine = null,
            Action<string>? onStdOutLine = null,
            Action<string>? onStdErrLine = null,
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

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            using var proc = new Process { StartInfo = psi };

            proc.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (sbOut) { sbOut.AppendLine(e.Data); }
                    onStdOutLine?.Invoke(e.Data);
                    onLine?.Invoke(e.Data);
                }
            };

            proc.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (sbErr) { sbErr.AppendLine(e.Data); }
                    onStdErrLine?.Invoke(e.Data);
                    onLine?.Invoke(e.Data);
                }
            };

            if (!proc.Start())
                throw new Exception($"Failed to start process: {fileName} {arguments}");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);

            return (proc.ExitCode, sbOut.ToString(), sbErr.ToString());
        }
    }
}
