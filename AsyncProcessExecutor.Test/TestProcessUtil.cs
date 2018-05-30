using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Buffers;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.Threading;
    using System.IO.Pipelines;
    [TestFixture]
    public class TestProcessUtil
    {
        [TestCase]
        public void TestExecuteAsyncNormal()
        {
            var procName = "cmd.exe";
            var arguments = "/c \"echo hogehoge\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"echo hogehoge\"";
            }
            var stdout = new Pipe();
            var retcode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments, stdout: stdout.Writer).Result;
            Assert.AreEqual(0, retcode);
        }
        [TestCase]
        public void TestExecuteAsyncReturnCode()
        {
            var procName = "cmd.exe";
            var arguments = "/c \"exit 1\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"exit 1\"";
            }
            var stdout = new Pipe();
            var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments, stdout: stdout.Writer).Result;
            Assert.AreEqual(1, retCode);
        }
        [TestCase]
        public void TestExecuteAsyncCancel()
        {
            var procName = "powershell.exe";
            var arguments = "Start-Sleep -Seconds 5";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"sleep 5\"";
            }
            using (var csrc = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                var pipe = new Pipe();
                try
                {
                    var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments, ctoken: csrc.Token, stdout: pipe.Writer).Result;
                    Assert.AreEqual(-1, retCode);
                }
                catch (AggregateException ae)
                {
                    Assert.IsTrue(ae.Flatten().InnerExceptions.OfType<TaskCanceledException>().Any());
                }
                catch (TaskCanceledException)
                {

                }
            }
        }
        [TestCase]
        public void TestExecuteAsyncWithWindow()
        {
            var procName = "notepad.exe";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
            }
            using (var csrc = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                try
                {
                    var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, "", ctoken: csrc.Token, createNoWindow: true).Result;
                    Assert.Fail("should not be reached");
                }
                catch (AggregateException ae)
                {
                    Assert.IsTrue(ae.Flatten().InnerExceptions.OfType<TaskCanceledException>().Any());
                }
                catch (TaskCanceledException)
                {

                }
            }
        }
        [TestCase]
        public void TestExecuteAsyncCommandNotFound()
        {
            var procName = "abcdefghijkl";
            Assert.Catch(typeof(AggregateException), () =>
            {
                AsyncProcessUtil.ExecuteProcessAsync(procName, null).Wait();
            });
        }
        [TestCase]
        public void TestExecuteAsyncInput()
        {
            var procName = "findstr";
            var arg = "hogehoge";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "grep";
            }
            var stdin = new System.Text.StringBuilder();
            var pipe = new Pipe();
            var stdout = new Pipe();
            stdin.AppendLine($"hogehogehoge");
            var data = Encoding.UTF8.GetBytes(stdin.ToString());
            pipe.Writer.Write(new Memory<byte>(data).Span);
            pipe.Writer.Complete();
            var ret = AsyncProcessUtil.ExecuteProcessAsync(procName, arg, stdin: pipe.Reader, stdout: stdout.Writer).Result;
            Assert.AreEqual(0, ret);
            var outbytes = stdout.Reader.GetAllBytes().Result;
            Assert.AreEqual("hogehogehoge", Encoding.UTF8.GetString(outbytes).Trim());
            // Assert.IsTrue(stdout.ToString().Trim() == "hogehogehoge");
        }
        [TestCase]
        public void TestExecuteAsyncManyInput()
        {
            var procName = "findstr";
            var arg = "hogehoge";
            var stdin = new Pipe();
            var stdout = new Pipe();
            var t = AsyncProcessUtil.ExecuteProcessAsync(procName, arg, stdin: stdin.Reader, stdout: stdout.Writer);
            using (var mstm = new MemoryStream())
            {
                Task.WaitAll(
                    t,
                    Task.Run(async () =>
                    {
                        var wbuf = ArrayPool<byte>.Shared.Rent(128);
                        for (int i = 0; i < 100; i++)
                        {
                            var str = $"hogehogehoge{i}";
                            var wlen = Encoding.UTF8.GetBytes(str, 0, str.Length, wbuf, 0);
                            stdin.Writer.Write(wbuf.AsSpan(0, wlen));
                            stdin.Writer.Advance(wlen);
                        }
                        await stdin.Writer.FlushAsync();
                    }).ContinueWith(x => stdin.Writer.Complete())
                    ,
                    Task.Run(async () =>
                    {
                        while (true)
                        {
                            var readresult = await stdout.Reader.ReadAsync();
                            if (!readresult.Buffer.IsEmpty)
                            {
                                foreach (var rbuf in readresult.Buffer)
                                {
#if NETCOREAPP_2_1
                                    mstm.Write(rbuf.Span);
#else
                                    var data = ArrayPool<byte>.Shared.Rent(rbuf.Length);
                                    try
                                    {
                                        rbuf.CopyTo(new Memory<byte>(data, 0, rbuf.Length));
                                        mstm.Write(data, 0, rbuf.Length);
                                    }
                                    finally
                                    {
                                        ArrayPool<byte>.Shared.Return(data);
                                    }
#endif
                                }
                                stdout.Reader.AdvanceTo(readresult.Buffer.End);
                            }
                            if (readresult.IsCompleted && readresult.Buffer.IsEmpty)
                            {
                                break;
                            }
                        }
                    })
                );
                Assert.AreNotEqual(0, mstm.Length);
            }
            // onOutput: (str) =>
            // {
            //     Console.WriteLine($"output '{str}'");
            //     stdout.Add(str);
            // },
            // inputCallback: (tw) =>
            // {
            //     for (int i = 0; i < 100; i++)
            //     {
            //         Console.WriteLine($"input line{i}");
            //         tw.WriteLine($"hogehogehoge{i}");
            //     }
            // }).Result;
            // insert blank line when procss finish
        }
    }
}
