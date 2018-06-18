using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.IO;
    using System.Text;
    using System.IO.Pipelines;
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
            var stderr = new Pipe();
            using (var res = AsyncProcessUtil.StartProcess(procname1, arg1)
            .DoNext(procname2, arg2, stderr: stderr.Writer)
            )
            {
                using (var mstm = new MemoryStream())
                {
                    Task.WaitAll(
                        Task.Run(async () =>
                        {
                            var bytes = await res.StandardOutput.GetAllBytes();
                            mstm.Write(bytes, 0, bytes.Length);
                            Console.WriteLine($"{Encoding.UTF8.GetString(mstm.ToArray())}");
                            bytes = await stderr.Reader.GetAllBytes();
                            Console.WriteLine($"{Encoding.UTF8.GetString(bytes)}");
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
        public async Task TestPipeStdOut()
        {
            var procname1 = "powershell";
            var arg1 = "\"[Console]::Out.WriteLine(\\\"hogehoge\\\")\"";
            var stderr = new Pipe();
            using (var ctx = AsyncProcessUtil.StartProcess(procname1, arg1, stderr: stderr.Writer))
            using (var mstm = new MemoryStream())
            {
                var exitCode = await ctx.WaitExit();
                var output = await ctx.StandardOutput.GetAllBytes();
                Console.WriteLine($"output = {Encoding.UTF8.GetString(output)}");
                Assert.AreEqual(8 + Environment.NewLine.Length, output.Length);
                var errout = await stderr.Reader.GetAllBytes();
                Console.WriteLine($"stderr = {Encoding.UTF8.GetString(errout)}");
                Assert.AreEqual(0, errout.Length);
                Assert.AreEqual(0, exitCode);
            }
        }
        [TestCase]
        public async Task TestPipeErrorOut()
        {
            var procname1 = "powershell";
            var arg1 = "\"[Console]::Error.WriteLine(\\\"hogehoge\\\")\"";
            var stderr = new Pipe();
            using (var ctx = AsyncProcessUtil.StartProcess(procname1, arg1, stderr: stderr.Writer))
            using (var mstm = new MemoryStream())
            {
                var exitCode = await ctx.WaitExit();
                var output = await ctx.StandardOutput.GetAllBytes();
                Console.WriteLine($"output = {Encoding.UTF8.GetString(output)}");
                Assert.AreEqual(0, output.Length);
                var errout = await stderr.Reader.GetAllBytes();
                Console.WriteLine($"stderr = {Encoding.UTF8.GetString(errout)}");
                Assert.AreEqual(8 + Environment.NewLine.Length, errout.Length);
                Assert.AreEqual(0, exitCode);
            }
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
                catch(AggregateException ae)
                {
                    ae = ae.Flatten();
                    Assert.IsTrue(ae.InnerExceptions.OfType<TaskCanceledException>().Any());
                }
                catch (TaskCanceledException e)
                {

                }
            }
        }
    }
}
