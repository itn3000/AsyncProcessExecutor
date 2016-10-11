using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor
{
    using System.Threading;
    using System.IO;
    using System.Diagnostics;
    public class AsyncProcessContext : IDisposable
    {
        public event Action<int> Exited;
        Task m_InputTask;
        Task m_ErrorOutputTask;
        ManualResetEventSlim m_Finished = new ManualResetEventSlim(false);
        /// <summary>
        /// process standard output stream
        /// </summary>
        /// <remarks>exception thrown when accessing this member after disposed</remarks>
        public Stream StandardOutput
        {
            get
            {
                return m_Process.StandardOutput.BaseStream;
            }
        }
        /// <summary>
        /// wait process finish and get result code
        /// </summary>
        /// <param name="onOutput">callback for standard output, do nothing if null</param>
        /// <returns>process result code,-1 if cancelled</returns>
        public async Task<int> WaitAsync(Func<Stream, CancellationToken, Task> onOutput = null)
        {
            await Task.WhenAll(m_InputTask != null ? m_InputTask : Task.FromResult<int>(0)
                , m_ErrorOutputTask != null ? m_ErrorOutputTask : Task.FromResult<int>(0)
                , Task.Run(async () =>
                {
                    if (onOutput != null)
                    {
                        try
                        {
                            await onOutput(this.StandardOutput, m_Token).ConfigureAwait(false);
                        }
                        catch (AggregateException e)
                        {
                            if (!(e.InnerException is OperationCanceledException))
                            {
                                throw;
                            }
                        }
                        catch (OperationCanceledException e)
                        {
                        }
                    }
                })
                ,
                Task.Run(() =>
                {
                    try
                    {
                        m_Finished.Wait(m_Token);
                    }
                    catch (OperationCanceledException e)
                    {
                    }
                })).ConfigureAwait(false);
            return this.ResultCode;
        }

        Stream StandardInput
        {
            get
            {
                return m_Process.StandardInput.BaseStream;
            }
        }
        Stream StandardError
        {
            get
            {
                return m_Process.StandardError.BaseStream;
            }
        }
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
        static IDictionary<string, string> m_Environments;
        public IDictionary<string, string> Environments
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
        internal AsyncProcessContext(ProcessStartInfo psi, CancellationToken ctoken, Func<Stream, CancellationToken, Task> input = null, Func<Stream, CancellationToken, Task> errorOut = null)
        {
            m_Token = ctoken;
#if NET45
            m_Environments = psi.EnvironmentVariables.Keys.Cast<string>()
                .Select(x => new { k = x, v = psi.EnvironmentVariables[x] })
                .ToDictionary(x => x.k, x => x.v)
                ;
#else
            m_Environments = psi.Environment;
#endif
            m_StartInfo = psi;
            var proc = new Process();
            try
            {
                proc.StartInfo = psi;
                m_Process = proc;
                proc.EnableRaisingEvents = true;
                proc.Exited += (sender, e) =>
                {
                    m_Finished.Set();
                    if (Exited != null)
                    {
                        Exited(m_Process.ExitCode);
                    }
                };
                m_TokenCancelledRegistration = ctoken.Register(() =>
                {
                    m_Finished.Set();
                    try
                    {
                        m_Process.Kill();
                    }
                    catch
                    {
                    }
                });

                proc.Start();
                if (input != null)
                {
                    m_InputTask = Task.Run(async () =>
                    {
                        try
                        {
                            await input(proc.StandardInput.BaseStream, ctoken).ConfigureAwait(false);
                            proc.StandardInput.Dispose();
                        }
                        catch (AggregateException e)
                        {
                            if (!(e.InnerException is OperationCanceledException))
                            {
                                throw;
                            }
                        }
                        catch (OperationCanceledException e)
                        {
                        }
                    });
                }
                if (errorOut != null)
                {
                    try
                    {
                        m_ErrorOutputTask = errorOut(proc.StandardError.BaseStream, ctoken);
                    }
                    catch (AggregateException e)
                    {
                        if (!(e.Flatten().InnerException is OperationCanceledException))
                        {
                            throw;
                        }
                    }
                    catch (OperationCanceledException e)
                    {
                    }
                }
            }
            catch
            {
                proc.Dispose();
                throw;
            }
        }
        Process m_Process;
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
                    if (m_Finished != null)
                    {
                        m_Finished.Dispose();
                    }
                    if(m_TokenCancelledRegistration != null)
                    {
                        m_TokenCancelledRegistration.Dispose();
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
