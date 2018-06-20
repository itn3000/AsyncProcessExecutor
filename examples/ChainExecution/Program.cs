using System;
using AsyncProcessExecutor;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.IO;
using System.Text;

namespace ChainExecution
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
        static void ExecutePipe()
        {
            var stderr = new Pipe();
            var stderr2 = new Pipe();
            // similar to `cmd.exe /c "echo abcde"|cmd.exe /c findstr a`
            using (var ctx = AsyncProcessUtil.StartProcess("cmd.exe", "/c \"echo abcde\"", stderr: stderr.Writer)
                .DoNext("cmd.exe", "/c findstr a", stderr: stderr2.Writer))
            {
                Task.WaitAll(
                    ctx.WaitExit().ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            Console.WriteLine($"exit code is {t.Result}");
                        }
                        else
                        {
                            Console.WriteLine($"exception:{t.Exception}");
                        }
                    }
                    ),
                    ctx.StandardOutput.GetAllBytes().ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            var str = Encoding.GetEncoding(932).GetString(t.Result);
                            Console.WriteLine($"stdout = {str}");
                        }
                        else
                        {
                            Console.WriteLine($"stdout exception:{t.Exception}");
                        }
                    })
                    ,
                    stderr2.Reader.GetAllBytes().ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            var str = Encoding.GetEncoding(932).GetString(t.Result);
                            Console.WriteLine($"stderr2 = {str}");
                        }
                        else
                        {
                            Console.WriteLine($"stderr2 exception:{t.Exception}");
                        }
                    })
                );
            }
        }
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ExecutePipe();
        }
    }
}
