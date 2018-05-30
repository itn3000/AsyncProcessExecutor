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
        CancellationToken m_Token;
        CancellationTokenRegistration m_TokenCancelledRegistration;
        ProcessStartInfo m_StartInfo;
        TaskCompletionSource<int> m_ProcessTask;
        Task m_InternalTask;
        Task StartOutputTask()
        {
            if (m_StandardOutputPipe != null)
            {
                return Task.Run(async () =>
                {
                    var buf = ArrayPool<byte>.Shared.Rent(1024);
                    try
                    {
                        while (!m_Token.IsCancellationRequested)
                        {
                            var bytesread = await m_Process.StandardOutput.BaseStream.ReadAsync(buf, 0, 1024);
                            if (bytesread == 0 && m_Process.HasExited)
                            {
                                break;
                            }
                            await m_StandardOutputPipe.WriteAsync(buf, m_Token).ConfigureAwait(false);
                            await m_StandardOutputPipe.FlushAsync(m_Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buf);
                    }
                }).ContinueWith(t =>
                {
                    if (m_StandardOutputPipe != null)
                    {
                        if (t.IsFaulted)
                        {
                            m_StandardOutputPipe.Complete(t.Exception);
                        }
                        else
                        {
                            m_StandardOutputPipe.Complete();
                        }
                    }
                });
            }
            else
            {
                return Task.CompletedTask;
            }
        }
        Task StartErrorOutputTask()
        {
            if (m_StandardErrorPipe != null)
            {
                return Task.Run(async () =>
                {
                    var buf = ArrayPool<byte>.Shared.Rent(1024);
                    try
                    {
                        while (!m_Token.IsCancellationRequested)
                        {
                            var bytesread = await m_Process.StandardError.BaseStream.ReadAsync(buf, 0, 1024);
                            if (bytesread == 0 && m_Process.HasExited)
                            {
                                break;
                            }
                            await m_StandardErrorPipe.WriteAsync(buf, m_Token).ConfigureAwait(false);
                            await m_StandardErrorPipe.FlushAsync(m_Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buf);
                    }
                }).ContinueWith(t =>
                {
                    if (m_StandardErrorPipe != null)
                    {
                        if (t.IsFaulted)
                        {
                            m_StandardErrorPipe.Complete(t.Exception);
                        }
                        else
                        {
                            m_StandardErrorPipe.Complete();
                        }
                    }
                });
            }
            else
            {
                return Task.CompletedTask;
            }
        }
        async Task StartTask()
        {
            if (!m_Process.Start())
            {
                throw new Exception("failed to start process");
            }
            await Task.WhenAll(
                Task.Run(() =>
                {
                    using (var csrc = new CancellationTokenSource())
                    using (var combined = CancellationTokenSource.CreateLinkedTokenSource(csrc.Token, m_Token))
                    {
                        m_Process.Exited += (sender, ev) =>
                        {
                            csrc.Cancel();
                        };
                        while (true)
                        {
                            if (m_StandardInput.TryRead(out var std))
                            {
                                if (std.IsCompleted && std.Buffer.IsEmpty)
                                {
                                    break;
                                }
                                if (!std.Buffer.IsEmpty)
                                {
                                    foreach (var rbuf in std.Buffer)
                                    {
#if NETCOREAPP2_1
                                        m_Process.StandardInput.BaseStream.Write(rbuf.Span);
#else
                                        var data = rbuf.ToArray();
                                        m_Process.StandardInput.BaseStream.Write(data, 0, data.Length);
#endif
                                    }
                                    m_StandardInput.AdvanceTo(std.Buffer.End);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
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
                }
                else if (t.IsCanceled)
                {
                    m_ProcessTask.TrySetCanceled();
                }
            }).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Exited?.Invoke(-1);
                    throw new AggregateException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    Exited?.Invoke(-2);
                    throw new OperationCanceledException();
                }
                else
                {
                    Exited?.Invoke(m_Process.ExitCode);
                }
            }).ConfigureAwait(false);
            if (Exited != null)
            {
                Exited(m_Process.ExitCode);
            }
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
                    if (m_Process != null)
                    {
                        m_Process.Dispose();
                    }
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
