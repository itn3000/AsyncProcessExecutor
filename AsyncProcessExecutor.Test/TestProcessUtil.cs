using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.Threading;
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
            var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments,
                onOutput: (str) =>
                {
                    Console.WriteLine("output:{0}", str);
                }
                , onErrorOutput: (str) =>
                {
                    Console.WriteLine("error:{0}", str);
                })
                .Result;
            Assert.AreEqual(0, retCode);
        }
        [TestCase]
        public void TestExecuteAsyncReturnCode()
        {
            var procName = "cmd.exe";
            var arguments = "/c \"exit 1\"";
            if(Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"exit 1\"";
            }
            var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments,
                onOutput: (str) =>
                {
                    Console.WriteLine("output:{0}", str);
                }
                , onErrorOutput: (str) =>
                {
                    Console.WriteLine("error:{0}", str);
                })
                .Result;
            Assert.AreEqual(1, retCode);
        }
        [TestCase]
        public void TestExecuteAsyncCancel()
        {
            var procName = "cmd.exe";
            var arguments = "/c \"timeout /T 5\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"sleep 5\"";
            }
            using (var csrc = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments,
                    onOutput: (str) =>
                    {
                        Console.WriteLine("output:{0}", str);
                    }
                    , onErrorOutput: (str) =>
                    {
                        Console.WriteLine("error:{0}", str);
                    }
                    , ctoken: csrc.Token)
                    .Result;
                Assert.AreEqual(-1, retCode);
            }
        }
        [TestCase]
        public void TestExecuteAsyncWithWindow()
        {
            var procName = "notepad.exe";
            if(Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
            }
            using (var csrc = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, "", ctoken: csrc.Token, createNoWindow: true).Result;
                Assert.AreEqual(-1, retCode);
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
            var stdout = new System.Text.StringBuilder();
            var ret = AsyncProcessUtil.ExecuteProcessAsync(procName, arg,
                onOutput: (str) =>
                {
                    stdout.Append(str);
                },
                inputCallback: (tw) =>
                {
                    tw.WriteLine("hogehogehoge");
                }).Result;
            Assert.IsTrue(stdout.ToString().Trim() == "hogehogehoge");
            Console.WriteLine($"output is '{stdout.ToString()}'");
        }
        [TestCase]
        public void TestExecuteAsyncManyInput()
        {
            var procName = "findstr";
            var arg = "hogehoge";
            var stdout = new List<string>();
            var ret = AsyncProcessUtil.ExecuteProcessAsync(procName, arg,
                onOutput: (str) =>
                {
                    Console.WriteLine($"output '{str}'");
                    stdout.Add(str);
                },
                inputCallback: (tw) =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        Console.WriteLine($"input line{i}");
                        tw.WriteLine($"hogehogehoge{i}");
                    }
                }).Result;
            // insert blank line when procss finish
            Assert.AreEqual(100 + 1, stdout.Count());
        }
    }
}
