using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncProcessExecutor
{
    using System.Diagnostics;
    using System.Threading;
    using System.IO;
    public static class AsyncProcessUtil
    {
        /// <summary>
        /// Execute process asynchronously
        /// </summary>
        /// <param name="fileName">execute file path</param>
        /// <param name="arg">execute argument</param>
        /// <param name="outputEncoding">process output encoding</param>
        /// <param name="inputCallback">callback for input standard input(invoke only once when process started)</param>
        /// <param name="onOutput">callback for process standard output</param>
        /// <param name="onErrorOutput">callback for process standard error output</param>
        /// <param name="ctoken">for cancelling execution</param>
        /// <param name="createNoWindow">flag for create window</param>
        /// <param name="leaveProcess">if true,leave process running when cancelled</param>
        /// <param name="env">additional environment variables</param>
        /// <returns>return exit code if process finished,-1 when cancelled</returns>
        public static async Task<int> ExecuteProcessAsync(string fileName
            , string arg
            , Encoding outputEncoding = null
            , Action<TextWriter> inputCallback = null
            , Action<string> onOutput = null
            , Action<string> onErrorOutput = null
            , CancellationToken ctoken = default(CancellationToken)
            , bool createNoWindow = false
            , bool leaveProcess = false
            , IDictionary<string, string> env = null
            )
        {
            var pi = new ProcessStartInfo(fileName, arg);
            pi.CreateNoWindow = createNoWindow;
            pi.UseShellExecute = false;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            if (outputEncoding != null)
            {
                pi.StandardErrorEncoding = outputEncoding;
                pi.StandardOutputEncoding = outputEncoding;
            }
            if (env != null)
            {
                foreach (var kv in env)
                {
#if NET45
                    pi.EnvironmentVariables[kv.Key] = kv.Value;
#else
                    pi.Environment[kv.Key] = kv.Value;
#endif
                }
            }
            using (var proc = new Process())
            using (var sem = new SemaphoreSlim(0, 1))
            using (ctoken.Register(() => sem.Release()))
            {
                proc.StartInfo = pi;
                proc.EnableRaisingEvents = true;
                if (onOutput != null)
                {
                    proc.OutputDataReceived += (sender, ev) =>
                    {
                        onOutput(ev.Data);
                    };
                }
                if (onErrorOutput != null)
                {
                    proc.ErrorDataReceived += (sender, ev) =>
                    {
                        onErrorOutput(ev.Data);
                    };
                }
                proc.Exited += (sender, ev) =>
                {
                    sem.Release();
                };
                proc.Start();
                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();
                inputCallback?.Invoke(proc.StandardInput);
                await sem.WaitAsync().ConfigureAwait(false);
                if (proc.HasExited)
                {
                    return proc.ExitCode;
                }
                else
                {
                    proc.CancelErrorRead();
                    proc.CancelOutputRead();
                    if (!leaveProcess)
                    {
                        proc.Kill();
                    }
                    return -1;
                }
            }
        }
    }
}
