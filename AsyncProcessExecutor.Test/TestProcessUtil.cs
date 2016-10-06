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
    }
}
