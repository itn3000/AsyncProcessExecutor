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
                    var exitCode = res.WaitAsync(async (stm, ctoken) =>
                    {
                        var buf = new byte[4096];
                        while(true)
                        {
                            var bytesread = await stm.ReadAsync(buf, 0, buf.Count()).ConfigureAwait(false);
                            var str = string.Join(":", buf.Take(bytesread).Select(x => x.ToString("X2")));
                            if(bytesread <= 0)
                            {
                                break;
                            }
                            Console.WriteLine($"{str}");
                        }
                    }).Result;
                    Assert.AreEqual(0, exitCode);
                }
            }
        }
        [TestCase]
        public void TestPipeErrorOut()
        {
            var procname1 = "powershell";
            var arg1 = "\"[Console]::Error.WriteLine(\\\"hogehoge\\\")\"";
            using (var proc = AsyncProcessUtil.StartProcess(procname1, arg1, errorOutputCallback: async (stm, ctoken) =>
             {
                 using (var mstm = new MemoryStream())
                 {
                     await stm.CopyToAsync(mstm).ConfigureAwait(false);
                     var str = Encoding.Default.GetString(mstm.ToArray());
                     Assert.IsTrue(str.Contains("hogehoge"));
                 }
             }
                ))
            {
                var exitCode = proc.WaitAsync().Result;
                Assert.AreEqual(0, exitCode);
            }

        }
        [TestCase]
        public void TestPipeCommandNotFound()
        {
            var procname1 = "powershell";
            var arg1 = "\"Write-Host hogehoge\"";
            Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            {
                using (var proc = AsyncProcessUtil.StartProcess(procname1, arg1
                    , inputCallback: async (stm, ctoken) =>
                    {
                        await Task.FromResult(0).ConfigureAwait(false);
                    }, errorOutputCallback: async (stm, ctoken) =>
                    {
                        using (var mstm = new MemoryStream())
                        {
                            await stm.CopyToAsync(mstm, 4096, ctoken).ConfigureAwait(false);
                            Console.WriteLine($"{Encoding.Default.GetString(mstm.ToArray())}");
                        }
                    }).DoNext("hogehoge", ""))
                {
                    var exitCode = proc.WaitAsync().Result;
                    Assert.AreEqual(-1, exitCode);
                }
            });
        }
        [TestCase]
        public void TestPipeCommandCancel()
        {
            using (var csrc = new CancellationTokenSource(1000))
            using (var proc = AsyncProcessUtil.StartProcess("powershell", "\"Start-Sleep 5\"", ctoken: csrc.Token))
            {
                var code = proc.WaitAsync().Result;
                Assert.AreEqual(-1, code);
            }
        }
    }
}
