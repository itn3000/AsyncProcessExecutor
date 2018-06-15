using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.IO;
    using System.Text;
    using System.Threading;
    [TestFixture]
    public class TestProcessPipe
    {
        [TestCase]
        public void TestPipe()
        {
            var procname1 = "cmd.exe";
            var arg1 = "/c \"echo hogehoge\"";
            var procname2 = "cmd.exe";
            var arg2 = "/c \"findstr h\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procname1 = "bash";
                arg1 = "-c \"echo hogehoge\"";
                procname2 = "cat";
                arg2 = "";
            }
            using (var res = AsyncProcessUtil.StartProcess(procname1, arg1)
            .DoNext(procname2, arg2))
            {
                using (var mstm = new MemoryStream())
                {
                    Task.WaitAll(
                        Task.Run(async () =>
                        {
                            while (true)
                            {
                                var readresult = await res.StandardOutput.ReadAsync().ConfigureAwait(false);
                                if (!readresult.Buffer.IsEmpty)
                                {
                                    foreach (var buf in readresult.Buffer)
                                    {
                                        var ar = buf.ToArray();
                                        mstm.Write(ar, 0, ar.Length);
                                    }
                                }
                                if (readresult.Buffer.IsEmpty && readresult.IsCompleted)
                                {
                                    break;
                                }
                            }
                            Console.WriteLine($"{Encoding.UTF8.GetString(mstm.ToArray())}");
                        }),
                        Task.Run(async () =>
                        {
                            var retcode = await res.WaitExit();
                            Assert.AreEqual(0, retcode);
                        })
                    );
                }
            }
        }
        [TestCase]
        public async Task TestPipeErrorOut()
        {
            var procname1 = "powershell";
            var arg1 = "\"[Console]::Error.WriteLine(\\\"hogehoge\\\")\"";
            using (var ctx = AsyncProcessUtil.StartProcess(procname1, arg1))
            using (var mstm = new MemoryStream())
            {
                while (true)
                {
                    var readResult = await ctx.StandardOutput.ReadAsync();
                    if (!readResult.Buffer.IsEmpty)
                    {
                        foreach (var buf in readResult.Buffer)
                        {
                            mstm.Write(buf.Span);
                        }
                    }
                    if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
                    {
                        break;
                    }
                }
                var exitCode = await ctx.WaitExit();
                Assert.AreEqual(0, exitCode);
            }
            // using (var proc = AsyncProcessUtil.StartProcess(procname1, arg1, errorOutputCallback: async (stm, ctoken) =>
            //  {
            //      using (var mstm = new MemoryStream())
            //      {
            //          await stm.CopyToAsync(mstm).ConfigureAwait(false);
            //          var str = Encoding.Default.GetString(mstm.ToArray());
            //          Assert.IsTrue(str.Contains("hogehoge"));
            //      }
            //  }
            //     ))
            // {
            //     var exitCode = proc.WaitAsync().Result;
            //     Assert.AreEqual(0, exitCode);
            // }

        }
        [TestCase]
        public void TestPipeCommandNotFound()
        {
            var procname1 = "powershel";
            var arg1 = "\"Write-Host hogehoge\"";
            Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            {
                using (var proc = AsyncProcessUtil.StartProcess(procname1, arg1))
                {
                    try
                    {
                        proc.WaitExit().Wait();
                        Assert.Fail("should not be reached");
                    }
                    catch (AggregateException ae)
                    {
                        throw ae.InnerException;
                    }
                }
            });
        }
        [TestCase]
        public void TestPipeCommandCancel()
        {
            using (var csrc = new CancellationTokenSource(1000))
            using (var proc = AsyncProcessUtil.StartProcess("powershell", "\"Start-Sleep 5\"", token: csrc.Token))
            {
                try
                {
                    var code = proc.WaitExit().Result;
                }
                catch (TaskCanceledException e)
                {

                }
            }
        }
    }
}
