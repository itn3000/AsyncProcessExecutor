using System;
using System.IO.Pipelines;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace BasicUsage
{
    static class PipelineExtension
    {
        public static async Task<byte[]> GetAllBytes(this PipeReader reader)
        {
            using (var mstm = new MemoryStream())
            {
                while (true)
                {
                    var readresult = await reader.ReadAsync().ConfigureAwait(false);
                    if (!readresult.Buffer.IsEmpty)
                    {
                        foreach (var rbuf in readresult.Buffer)
                        {
                            mstm.Write(rbuf.Span);
                        }
                        reader.AdvanceTo(readresult.Buffer.End);
                    }
                    if (readresult.IsCompleted && readresult.Buffer.IsEmpty)
                    {
                        break;
                    }
                }
                reader.Complete();
                return mstm.ToArray();
            }
        }
    }
    class Program
    {
        static void ExecuteWithStandardInput()
        {
            var stdin = new Pipe();
            var stdout = new Pipe();
            Task.WaitAll(
                AsyncProcessExecutor.AsyncProcessUtil.ExecuteProcessAsyncBinary("powershell.exe"
                    , "[Console]::ReadLine()", stdin: stdin.Reader, stdout: stdout.Writer).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        Console.WriteLine($"exitcode is {t.Result}");
                    }
                    else
                    {
                        Console.WriteLine($"exception:{t.Exception}");
                    }
                }),
                Task.Run(() =>
                {
                    var mem = stdin.Writer.GetSpan(32);
                    Span<char> buf = stackalloc char[32];
                    buf[0] = 's';
                    buf[1] = 'あ';
                    buf[2] = 'd';
                    buf[3] = 'i';
                    buf[4] = 'n';
                    var bytelen = Encoding.UTF8.GetBytes(buf.Slice(0, 5), mem);
                    stdin.Writer.Advance(bytelen);
                    stdin.Writer.Complete();
                    Console.WriteLine($"input done");
                }),
                stdout.Reader.GetAllBytes().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        // will be 'sあdin'(utf8 encoded)
                        var str = Encoding.UTF8.GetString(t.Result);
                        Console.WriteLine($"stdin: '{str}'");
                    }
                    else
                    {
                        Console.WriteLine($"exception: {t.Exception}");
                    }
                })
            );
        }
        static void ExecuteAndGetStandardError()
        {
            var stdin = new Pipe();
            var stderr = new Pipe();
            var t = AsyncProcessExecutor.AsyncProcessUtil.ExecuteProcessAsyncBinary("powershell.exe",
                "[Console]::Error.WriteLine(\"'abcde'\")", stderr: stderr.Writer);
            Task.WaitAll(
                t.ContinueWith(tx =>
                {
                    if (tx.IsCompletedSuccessfully)
                    {
                        Console.WriteLine($"exit code is {tx.Result}");
                    }
                    else
                    {
                        Console.WriteLine($"exception:{tx.Exception}");
                    }
                }),
                stderr.Reader.GetAllBytes().ContinueWith(tx =>
                {
                    // will get abcde
                    if (tx.IsCompletedSuccessfully)
                    {
                        var str = System.Text.Encoding.GetEncoding(932).GetString(tx.Result);
                        Console.WriteLine($"stderr is '{str}'");
                    }
                    else
                    {
                        Console.WriteLine($"exception:{tx.Exception}");
                    }
                })
            );
        }
        static void ExecuteAndGetStandardOutput()
        {
            var stdin = new Pipe();
            var stdout = new Pipe();
            var t = AsyncProcessExecutor.AsyncProcessUtil.ExecuteProcessAsyncBinary("powershell.exe",
                "[Console]::WriteLine(\"'abcde'\")", stdout: stdout.Writer);
            Task.WaitAll(
                t.ContinueWith(tx =>
                {
                    if (tx.IsCompletedSuccessfully)
                    {
                        Console.WriteLine($"exit code is {tx.Result}");
                    }
                    else
                    {
                        Console.WriteLine($"exception:{tx.Exception}");
                    }
                }),
                stdout.Reader.GetAllBytes().ContinueWith(tx =>
                {
                    // will get abcde
                    if (tx.IsCompletedSuccessfully)
                    {
                        var str = System.Text.Encoding.GetEncoding(932).GetString(tx.Result);
                        Console.WriteLine($"stdout is '{str}'");
                    }
                    else
                    {
                        Console.WriteLine($"exception:{tx.Exception}");
                    }
                })
            );
        }
        static void Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            ExecuteAndGetStandardOutput();
            ExecuteAndGetStandardError();
            ExecuteWithStandardInput();
        }
    }
}
