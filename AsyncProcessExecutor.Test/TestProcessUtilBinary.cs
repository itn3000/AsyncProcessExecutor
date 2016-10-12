using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.Text;
    using System.Threading;
    [TestFixture]
    public class TestProcessUtilBinary
    {
        [TestCase]
        public void TestExecuteAsyncBinarySimple()
        {
            var procName = "cmd.exe";
            var arg = "/c \"echo hogehoge\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arg = "-c \"echo hogehoge\"";
            }
            var ret = AsyncProcessUtil.ExecuteProcessAsyncBinary(procName, arg).Result;
            Assert.AreEqual(0, ret);
        }
        [TestCase]
        public void TestExecuteAsyncBinaryExitCode()
        {
            var procName = "cmd.exe";
            var arguments = "/c \"exit 1\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arguments = "-c \"exit 1\"";
            }
            var ret = AsyncProcessUtil.ExecuteProcessAsyncBinary(procName, arguments).Result;
            Assert.AreEqual(1, ret);
        }
        [TestCase]
        public void TestExecuteAsyncBinaryOutput()
        {
            var procName = "cmd.exe";
            var arg = "/c \"echo abcde\"";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "bash";
                arg = "-c \"echo abcde\"";
            }
            var resultBinary = new List<byte>();
            var ret = AsyncProcessUtil.ExecuteProcessAsyncBinary(procName, arg
                , onOutput: (b) =>
                {
                    resultBinary.AddRange(b);
                }).Result;
            Assert.AreEqual('a', (char)resultBinary[0]);
            Assert.AreEqual('b', (char)resultBinary[1]);
            Assert.AreEqual('c', (char)resultBinary[2]);
            Assert.AreEqual('d', (char)resultBinary[3]);
            Assert.AreEqual('e', (char)resultBinary[4]);
            Console.WriteLine($"resultlength = {resultBinary.Count}");
        }
        [TestCase]
        public void TestExecuteAsyncBinaryInput()
        {
            var procName = "findstr";
            var arg = "a";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                procName = "grep";
            }
            var resultBinary = new List<byte>();
            var inputData = Encoding.UTF8.GetBytes("abcde").Concat(new byte[] { 0x1 }).ToArray();
            var ret = AsyncProcessUtil.ExecuteProcessAsyncBinary(procName, arg
                , inputCallback: (stm) =>
                {
                    stm.Write(inputData, 0, inputData.Count());
                }
                , onOutput: (b) =>
                {
                    resultBinary.AddRange(b);
                }).Result;
            Assert.AreEqual('a', (char)resultBinary[0]);
            Assert.AreEqual('b', (char)resultBinary[1]);
            Assert.AreEqual('c', (char)resultBinary[2]);
            Assert.AreEqual('d', (char)resultBinary[3]);
            Assert.AreEqual('e', (char)resultBinary[4]);
            Assert.AreEqual(0x1, resultBinary[5]);
            Console.WriteLine($"resultlength = {resultBinary.Count}");
        }
        [TestCase, Timeout(1500)]
        public void TestExecuteAsyncBinaryCancel()
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
                var retCode = AsyncProcessUtil.ExecuteProcessAsync(procName, arguments
                    , ctoken: csrc.Token)
                    .Result;
                Assert.AreEqual(-1, retCode);
            }
        }
    }
}
