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
        static ProcessStartInfo CreateStartInfo(
            string fileName
            , string arg
            , bool createNoWindow
            , Action<TextWriter> inputCallback
            , Encoding outputEncoding
            , IDictionary<string, string> env)
        {
            var pi = new ProcessStartInfo(fileName, arg);
            pi.CreateNoWindow = createNoWindow;
            pi.UseShellExecute = false;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            pi.RedirectStandardInput = inputCallback != null;
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
            return pi;
        }
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
            var pi = CreateStartInfo(fileName, arg, createNoWindow, inputCallback, outputEncoding, env);
            using (var proc = new Process())
            using (var sem = new SemaphoreSlim(0, 1))
            using (ctoken.Register(() => sem.Release()))
            {
                try
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
                        try
                        {
                            sem.Release();
                        }
                        catch
                        {
                        }
                    };
                    proc.Start();
                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();
                    if (inputCallback != null)
                    {
                        await Task.Run(() =>
                        {
                            inputCallback(proc.StandardInput);
                        }).ConfigureAwait(false);
                        proc.StandardInput.Dispose();
                    }
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
                catch
                {
                    if (!leaveProcess && !proc.HasExited)
                    {
                        proc.Kill();
                    }
                    throw;
                }
            }
        }
        public static async Task<int> ExecuteProcessAsyncBinary(string fileName
            , string arg
            , Action<Stream> inputCallback = null
            , Action<byte[]> onOutput = null
            , Action<byte[]> onOutputError = null
            , CancellationToken ctoken = default(CancellationToken)
            , bool createNoWindow = true
            , bool leaveProcess = false
            , IDictionary<string, string> env = null
            , int bufferSize = 4096)
        {
            var pi = CreateStartInfo(fileName, arg, createNoWindow, null, null, env);
            pi.RedirectStandardError = onOutputError != null;
            pi.RedirectStandardInput = inputCallback != null;
            pi.RedirectStandardOutput = onOutput != null;
            using (var proc = new Process())
            using (var sem = new SemaphoreSlim(0, 1))
            using (ctoken.Register(() => sem.Release()))
            {
                try
                {
                    proc.StartInfo = pi;
                    proc.Exited += (sender, e) =>
                    {
                        try
                        {
                            sem.Release();
                        }
                        catch
                        {
                        }
                    };
                    proc.EnableRaisingEvents = true;
                    proc.Start();
                    var inputTask = Task.Run(async () =>
                    {
                        await Task.FromResult(0).ConfigureAwait(false);
                        if (inputCallback != null)
                        {
                            inputCallback(proc.StandardInput.BaseStream);
                            proc.StandardInput.Dispose();
                        }
                    });
                    var outputTask = Task.Run(async () =>
                    {
                        if (onOutput != null)
                        {
                            await ReadStreamUntilCancel(proc.StandardOutput.BaseStream, onOutput, bufferSize, ctoken).ConfigureAwait(false);
                        }
                    });
                    var errorOutTask = Task.Run(async () =>
                    {
                        if (onOutputError != null)
                        {
                            await ReadStreamUntilCancel(proc.StandardError.BaseStream, onOutputError, bufferSize, ctoken).ConfigureAwait(false);
                        }
                    });
                    await sem.WaitAsync().ConfigureAwait(false);
                    await Task.WhenAll(inputTask, outputTask, errorOutTask).ConfigureAwait(false);
                    if (proc.HasExited)
                    {
                        return proc.ExitCode;
                    }
                    else
                    {
                        return -1;
                    }
                }
                finally
                {
                    if (!leaveProcess && !proc.HasExited)
                    {
                        proc.Kill();
                    }
                }
            }
        }
        static async Task ReadStreamUntilCancel(Stream stm, Action<byte[]> callBack, int bufferSize, CancellationToken ctoken)
        {
            if (callBack != null)
            {
                var buf = new byte[bufferSize];
                try
                {
                    while (!ctoken.IsCancellationRequested)
                    {
                        var bytesread = await stm.ReadAsync(buf, 0, bufferSize, ctoken).ConfigureAwait(false);
                        if (bytesread <= 0)
                        {
                            break;
                        }
                        callBack(buf.Take(bytesread).ToArray());
                    }
                }
                catch (AggregateException e)
                {
                    if (e.InnerException is OperationCanceledException)
                    {
                        return;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        public static AsyncProcessContext DoNext(this AsyncProcessContext t, string fileName, string arg
            , bool createNoWindow = true
            , IDictionary<string, string> env = null
            , Func<Stream, CancellationToken, Task> errorOutputCallback = null
            , CancellationToken ctoken = default(CancellationToken))
        {
            var newProc = StartProcess(fileName, arg, createNoWindow: createNoWindow, env: env, inputCallback: async (stm, token) =>
                  {
                      await t.StandardOutput.CopyToAsync(stm, 4096, token).ConfigureAwait(false);
                  }, errorOutputCallback: errorOutputCallback, ctoken: ctoken);
            newProc.Exited += (code) =>
            {
                t.Dispose();
            };
            return newProc;
        }
        public static AsyncProcessContext StartProcess(
            string fileName
            , string arguments
            , bool createNoWindow = true
            , IDictionary<string, string> env = null
            , Func<Stream, CancellationToken, Task> inputCallback = null
            , Func<Stream, CancellationToken, Task> errorOutputCallback = null
            , CancellationToken ctoken = default(CancellationToken))
        {
            var pi = CreateStartInfo(fileName, arguments, createNoWindow, null, null, env);
            pi.RedirectStandardError = true;
            pi.RedirectStandardInput = true;
            pi.RedirectStandardOutput = true;
            var ret = new AsyncProcessContext(pi, ctoken, inputCallback, errorOutputCallback);
            return ret;
        }
    }
}
