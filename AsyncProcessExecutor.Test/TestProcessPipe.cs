using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncProcessExecutor.Test
{
    using NUnit.Framework;
    using System.IO;
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
            using (var res = AsyncProcessUtil.StartProcess(procname1, arg1, true, null, null)
            .DoNext(procname2, arg2, true, null))
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
    }
}
