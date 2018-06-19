using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor
{
    using System.Buffers;
    using System.Threading;
    using System.IO;
    using System.IO.Pipelines;
    using System.Diagnostics;
    public class AsyncProcessContext : IDisposable
    {
        public event Action<int> Exited;

        CancellationToken Token
        {
            get
            {
                return m_Token;
            }
        }
        public string FileName
        {
            get
            {
                return m_StartInfo.FileName;
            }
        }
        public string Argument
        {
            get
            {
                return m_StartInfo.Arguments;
            }
        }
        IReadOnlyDictionary<string, string> m_Environments;
        public IReadOnlyDictionary<string, string> Environments
        {
            get
            {
                return m_Environments;
            }
        }
        public int ResultCode
        {
            get
            {
                if (m_Process == null || !m_Process.HasExited)
                {
                    return -1;
                }
                else
                {
                    return m_Process.ExitCode;
                }
            }
        }
        public Task<int> WaitExit()
        {
            return m_ProcessTask.Task;
        }
        CancellationToken m_Token;
        ProcessStartInfo m_StartInfo;
        TaskCompletionSource<int> m_ProcessTask = new TaskCompletionSource<int>(TaskContinuationOptions.RunContinuationsAsynchronously);
        Task m_InternalTask;
        async Task StartOutputTask()
        {
            if (m_StandardOutputPipe != null)
            {
                var buf = ArrayPool<byte>.Shared.Rent(1024);
                try
                {
                    while (true)
                    {
                        var bytesread = await m_Process.StandardOutput.BaseStream.ReadAsync(buf, 0, 1024, m_Token).ConfigureAwait(false);
                        if (bytesread == 0 && m_Process.HasExited)
                        {
                            break;
                        }
                        await m_StandardOutputPipe.WriteAsync(new Memory<byte>(buf, 0, bytesread), m_Token).ConfigureAwait(false);
                        await m_StandardOutputPipe.FlushAsync(m_Token).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException e)
                {
                    m_StandardOutputPipe.Complete(e);
                }
                catch (Exception e)
                {
                    m_StandardOutputPipe.Complete(e);
                    throw;
                }
                finally
                {
                    m_StandardOutputPipe.Complete();
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }
        async Task StartErrorOutputTask()
        {
            if (m_StandardErrorPipe != null)
            {
                var buf = ArrayPool<byte>.Shared.Rent(1024);
                try
                {
                    while (true)
                    {
                        var bytesread = await m_Process.StandardError.BaseStream.ReadAsync(buf, 0, 1024, m_Token).ConfigureAwait(false);
                        if (bytesread == 0 && m_Process.HasExited)
                        {
                            break;
                        }
                        if (bytesread != 0)
                        {
                            await m_StandardErrorPipe.WriteAsync(new Memory<byte>(buf, 0, bytesread), m_Token).ConfigureAwait(false);
                            await m_StandardErrorPipe.FlushAsync(m_Token);
                        }
                    }
                }
                catch (TaskCanceledException e)
                {
                    m_StandardErrorPipe.Complete(e);
                }
                catch (Exception e)
                {
                    m_StandardErrorPipe.Complete(e);
                    throw;
                }
                finally
                {
                    m_StandardErrorPipe.Complete();
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }
        async ValueTask InputTask()
        {
            if (m_StandardInput != null)
            {
                try
                {
                    while (true)
                    {
                        var std = await m_StandardInput.ReadAsync(m_Token).ConfigureAwait(false);
                        if (!std.Buffer.IsEmpty)
                        {
                            foreach (var rbuf in std.Buffer)
                            {
#if NETCOREAPP2_1
                                m_Process.StandardInput.BaseStream.Write(rbuf.Span);
#else
                                var data = rbuf.ToArray();
                                await m_Process.StandardInput.BaseStream.WriteAsync(data, 0, data.Length, m_Token).ConfigureAwait(false);
#endif
                            }
                            m_StandardInput.AdvanceTo(std.Buffer.End);
                        }
                        if (std.IsCompleted && std.Buffer.IsEmpty)
                        {
                            break;
                        }
                    }
                }
                catch (TaskCanceledException e)
                {
                    m_StandardInput.Complete(e);
                    return;
                }
                catch (Exception e)
                {
                    m_StandardInput.Complete(e);
                    throw;
                }
                finally
                {
                    m_Process.StandardInput.Dispose();
                }
            }
        }
        async Task StartTask()
        {
            try
            {
                if (!m_Process.Start())
                {
                    m_ProcessTask.TrySetException(new InvalidOperationException($"failed to start process(exe={m_StartInfo.FileName},arg={m_StartInfo.Arguments}"));
                    return;
                }
            }
            catch (Exception e)
            {
                m_ProcessTask.TrySetException(e);
                return;
            }
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    using (var csrc = new CancellationTokenSource())
                    using (var combined = CancellationTokenSource.CreateLinkedTokenSource(csrc.Token, m_Token))
                    {
                        m_Process.Exited += (sender, ev) =>
                        {
                            csrc.Cancel();
                        };
                        await InputTask().ConfigureAwait(false);
                        combined.Token.WaitHandle.WaitOne();
                        if (m_Token.IsCancellationRequested)
                        {
                            m_ProcessTask.TrySetCanceled();
                            return;
                        }
                    }
                    m_ProcessTask.TrySetResult(m_Process.ExitCode);
                }),
                StartOutputTask(),
                StartErrorOutputTask()
            ).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    m_ProcessTask.TrySetException(t.Exception);
                    Exited?.Invoke(-1);
                }
                else if (t.IsCanceled)
                {
                    m_ProcessTask.TrySetCanceled();
                    Exited?.Invoke(-2);
                }
                else
                {
                    Exited?.Invoke(m_Process.ExitCode);
                }
            }).ConfigureAwait(false);
        }
        internal AsyncProcessContext(ProcessStartInfo psi, CancellationToken ctoken, PipeReader stdin, PipeWriter stderr)
        {
            m_Process = new Process();
            m_Token = ctoken;
            if (psi.RedirectStandardOutput)
            {
                m_InternalStandardOutput = new Pipe();
            }
            if (psi.RedirectStandardError)
            {
                m_StandardErrorPipe = stderr;
            }
            if (psi.RedirectStandardInput)
            {
                m_StandardInput = stdin;
            }
            m_Process.StartInfo = psi;
            m_StartInfo = psi;
            m_InternalTask = StartTask();
        }
        Pipe m_InternalStandardOutput;
        public PipeReader StandardOutput => m_InternalStandardOutput?.Reader;
        Process m_Process;
        PipeWriter m_StandardOutputPipe => m_InternalStandardOutput?.Writer;
        PipeWriter m_StandardErrorPipe;
        PipeReader m_StandardInput;

        #region IDisposable Support
        private bool disposedValue = false; // 重複する呼び出しを検出するには

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        if (m_Process != null)
                        {
                            m_Process.Dispose();
                        }
                        m_StandardErrorPipe?.Complete();
                        m_StandardOutputPipe?.Complete();
                    }
                    catch { }
                }

                disposedValue = true;
            }
        }


        // TODO: 上の Dispose(bool disposing) にアンマネージ リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします。
        // ~AsyncProcessContext() {
        //   // このコードを変更しないでください。クリーンアップ コードを上の Dispose(bool disposing) に記述します。
        //   Dispose(false);
        // }

        // このコードは、破棄可能なパターンを正しく実装できるように追加されました。
        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを上の Dispose(bool disposing) に記述します。
            Dispose(true);
            // TODO: 上のファイナライザーがオーバーライドされる場合は、次の行のコメントを解除してください。
            //GC.SuppressFinalize(this);
        }
        #endregion
    }
}
