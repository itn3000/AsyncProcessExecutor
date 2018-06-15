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
    using System.IO.Pipelines;
    using System.Buffers;
    public static class AsyncProcessUtil
    {
        static async Task OutputTask(Process proc, PipeWriter output, CancellationToken token)
        {
            if (output == null)
            {
                return;
            }
            var buf = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                long total = 0;
                while (true)
                {
                    var bytesread = await proc.StandardOutput.BaseStream.ReadAsync(buf, 0, 4096, token).ConfigureAwait(false);
                    if (bytesread <= 0)
                    {
                        return;
                    }
                    output.Write(new Span<byte>(buf, 0, bytesread));
                    total += bytesread;
                    if (total > 4096)
                    {
                        await output.FlushAsync(token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
                output.Complete();
            }
        }
        static async Task InputTask(Process proc, PipeReader stdin, CancellationToken token)
        {
            if (stdin == null)
            {
                return;
            }
            while (true)
            {
                var readresult = await stdin.ReadAsync(token).ConfigureAwait(false);
                if (!readresult.Buffer.IsEmpty)
                {
                    foreach (var rbuf in readresult.Buffer)
                    {
#if NETCOREAPP_2_1
                        proc.StandardInput.BaseStream.Write(rbuf.Span);
#else
                        var buf = ArrayPool<byte>.Shared.Rent(rbuf.Length);
                        try
                        {
                            rbuf.CopyTo(new Memory<byte>(buf));
                            proc.StandardInput.BaseStream.Write(buf, 0, rbuf.Length);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buf);
                        }
#endif
                    }
                }
            }
        }
        /// execute process asynchronously with binary output
        public static async Task<int> ExecuteProcessAsyncBinary(string fileName,
            string args,
            bool createNoWindow = true,
            IReadOnlyDictionary<string, string> env = null,
            PipeWriter stdout = null,
            PipeWriter stderr = null,
            PipeReader stdin = null,
            CancellationToken token = default(CancellationToken)
        )
        {
            var si = CreateStartInfo(fileName, args, createNoWindow, stdin, Encoding.UTF8, env);
            si.RedirectStandardError = stderr != null;
            si.RedirectStandardOutput = stdout != null;
            si.RedirectStandardInput = stdin != null;
            using (var proc = new Process())
            using (var csrc = new CancellationTokenSource())
            using (var combined = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                proc.StartInfo = si;
                proc.EnableRaisingEvents = true;
                proc.Exited += (sender, ev) =>
                {
                    csrc.Cancel();
                };
                await Task.WhenAll(
                    OutputTask(proc, stdout, token),
                    OutputTask(proc, stderr, token),
                    InputTask(proc, stdin, token)
                    ,
                    Task.Run(() =>
                    {
                        try
                        {
                            combined.Token.WaitHandle.WaitOne();
                        }
                        catch
                        {
                        }
                    })
                );
                return proc.HasExited ? proc.ExitCode : -1;
            }
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
            , PipeWriter stdout = null
            , PipeWriter stderr = null
            , PipeReader stdin = null
            , CancellationToken ctoken = default(CancellationToken)
            , bool createNoWindow = false
            , bool leaveProcess = false
            , IDictionary<string, string> env = null
            )
        {
            outputEncoding = outputEncoding ?? Encoding.UTF8;
            var pi = CreateStartInfo(fileName, arg, createNoWindow, stdin, outputEncoding, env);
            pi.RedirectStandardInput = stdin != null;
            pi.RedirectStandardError = stderr != null;
            pi.RedirectStandardOutput = stdout != null;
            using (var proc = new Process())
            using (var sem = new SemaphoreSlim(0, 1))
            using (ctoken.Register(() => sem.Release()))
            {
                try
                {
                    proc.StartInfo = pi;
                    proc.EnableRaisingEvents = true;
                    if (stdout != null)
                    {
                        proc.OutputDataReceived += (sender, ev) =>
                        {
                            var len = outputEncoding.GetByteCount(ev.Data);
                            var buf = ArrayPool<byte>.Shared.Rent(len);
                            try
                            {
                                var bc = outputEncoding.GetBytes(ev.Data, 0, ev.Data.Length, buf, 0);
                                stdout.Write(new Span<byte>(buf, 0, bc));
                                stdout.FlushAsync().GetAwaiter().GetResult();
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buf);
                            }
                        };
                    }
                    if (stderr != null)
                    {
                        proc.ErrorDataReceived += (sender, ev) =>
                        {
                            var len = outputEncoding.GetByteCount(ev.Data);
                            var buf = ArrayPool<byte>.Shared.Rent(len);
                            try
                            {
                                var bc = outputEncoding.GetBytes(ev.Data, 0, ev.Data.Length, buf, 0);
                                stderr.Write(new Span<byte>(buf, 0, bc));
                                stderr.FlushAsync().GetAwaiter().GetResult();
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buf);
                            }
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
                    
                    while(true)
                    {
                        var readresult = 
                    }
                    await sem.WaitAsync().ConfigureAwait(false);
                    proc.CancelErrorRead();
                    proc.CancelOutputRead();
                    if (proc.HasExited)
                    {
                        return proc.ExitCode;
                    }
                    else
                    {
                        if (!leaveProcess)
                        {
                            proc.Kill();
                        }
                        return -1;
                    }
                }
                catch
                {
                    try
                    {
                        if (!leaveProcess && !proc.HasExited)
                        {
                            proc.Kill();
                        }
                    }
                    catch
                    {
                    }
                    throw;
                }
            }
        }
        /// <summary>
        /// execute process,handling process input/output as binary
        /// </summary>
        /// <param name="fileName">path to process binary file</param>
        /// <param name="arg">process arguments</param>
        /// <param name="inputCallback">call when process begin,passed write-only stream</param>
        /// <param name="onOutput">callback to get standard output data</param>
        /// <param name="onOutputError">callback to get standard error data</param>
        /// <param name="ctoken">if you want to cancel process, pass the CancellationToken</param>
        /// <param name="createNoWindow">flag for creating window(affected only windows)</param>
        /// <param name="leaveProcess">flag for not killing process when cancelled</param>
        /// <param name="env">additional environments for process</param>
        /// <param name="bufferSize">buffer size for output</param>
        /// <returns>process exit code, -1 when cancelled</returns>
        public static AsyncProcessContext StartProcess(string fileName,
            string arguments,
            bool createNoWindow = true,
            IReadOnlyDictionary<string, string> env = null,
            PipeReader stdin = null,
            PipeWriter stderr = null,
            CancellationToken token = default(CancellationToken))
        {
            var pi = CreateStartInfo(fileName, arguments, createNoWindow, stdin, null, env);
            pi.RedirectStandardError = stderr != null;
            pi.RedirectStandardInput = stdin != null;
            pi.RedirectStandardOutput = true;
            return new AsyncProcessContext(pi, token, stdin, stderr);
        }
        /// <summary>
        /// extension method for fluent process execution,all standard output in AsyncProcessContext is redirected to next process standard input
        /// </summary>
        /// <remarks>previous process will be killed when new process finished</remarks>
        /// <param name="t"></param>
        /// <param name="fileName">next process binary path</param>
        /// <param name="arg">next process argument</param>
        /// <param name="createNoWindow">flag for creating window</param>
        /// <param name="env">additional environment variables for process</param>
        /// <param name="errorOutputCallback">standard error callback for next process</param>
        /// <param name="ctoken">used for cancel process</param>
        /// <returns>next process AsyncProcessContext</returns>
        // public static AsyncProcessContext DoNext(this AsyncProcessContext t, string fileName, string arg
        //     , bool createNoWindow = true
        //     , IDictionary<string, string> env = null
        //     , Func<Stream, CancellationToken, Task> errorOutputCallback = null
        //     , CancellationToken ctoken = default(CancellationToken))
        // {
        //     var newProc = StartProcess(fileName, arg, createNoWindow: createNoWindow, env: env, inputCallback: async (stm, token) =>
        //     {
        //         // await t.StandardOutput.(stm, 4096, token).ConfigureAwait(false);
        //     }, errorOutputCallback: errorOutputCallback, ctoken: ctoken);
        //     newProc.Exited += (code) =>
        //     {
        //         t.Dispose();
        //     };
        //     return newProc;
        // }
        public static AsyncProcessContext DoNext(this AsyncProcessContext t, string fileName, string arg
            , bool createNoWindow = true
            , IReadOnlyDictionary<string, string> env = null
            , PipeWriter stderr = null
            , CancellationToken ctoken = default(CancellationToken))
        {
            var newProc = StartProcess(fileName, arg, createNoWindow: createNoWindow, env: env, stdin: t.StandardOutput, stderr: stderr, token: ctoken);
            newProc.Exited += (exitCode) => t.Dispose();
            return newProc;
        }
        //         static ProcessStartInfo CreateStartInfo(
        //             string fileName
        //             , string arg
        //             , bool createNoWindow
        //             , Action<TextWriter> inputCallback
        //             , Encoding outputEncoding
        //             , IDictionary<string, string> env)
        //         {
        //             var pi = new ProcessStartInfo(fileName, arg);
        //             pi.CreateNoWindow = createNoWindow;
        //             pi.UseShellExecute = false;
        //             pi.RedirectStandardError = true;
        //             pi.RedirectStandardOutput = true;
        //             pi.RedirectStandardInput = inputCallback != null;
        //             if (outputEncoding != null)
        //             {
        //                 pi.StandardErrorEncoding = outputEncoding;
        //                 pi.StandardOutputEncoding = outputEncoding;
        //             }
        //             if (env != null)
        //             {
        //                 foreach (var kv in env)
        //                 {
        // #if NET45
        //                     pi.EnvironmentVariables[kv.Key] = kv.Value;
        // #else
        //                     pi.Environment[kv.Key] = kv.Value;
        // #endif
        //                 }
        //             }
        //             return pi;
        //         }
        static ProcessStartInfo CreateStartInfo(
            string fileName
            , string arg
            , bool createNoWindow
            , PipeReader stdin
            , Encoding outputEncoding
            , IReadOnlyDictionary<string, string> env)
        {
            var pi = new ProcessStartInfo(fileName, arg);
            pi.CreateNoWindow = createNoWindow;
            pi.UseShellExecute = false;
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            pi.RedirectStandardInput = stdin != null;
            if (env != null)
            {
                foreach (var kv in env)
                {
                    pi.Environment[kv.Key] = kv.Value;
                }
            }
            return pi;
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
    }
}
