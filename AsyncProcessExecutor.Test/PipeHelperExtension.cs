using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using System.Threading;

namespace AsyncProcessExecutor.Test
{
    public static class PipeHelperExtension
    {
        public static async Task<byte[]> GetAllBytes(this PipeReader reader, CancellationToken ct = default(CancellationToken))
        {
            using (var mstm = new MemoryStream())
            {
                while (true)
                {
                    var readresult = await reader.ReadAsync(ct);
                    if (!readresult.Buffer.IsEmpty)
                    {
                        foreach (var rbuf in readresult.Buffer)
                        {
                            mstm.Write(rbuf.Span);
                        }
                        reader.AdvanceTo(readresult.Buffer.End);
                    }
                    if(readresult.IsCompleted && readresult.Buffer.IsEmpty)
                    {
                        break;
                    }
                }
                return mstm.ToArray();
            }
        }
    }
}