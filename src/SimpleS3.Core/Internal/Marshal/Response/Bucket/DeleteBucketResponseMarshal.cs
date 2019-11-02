using System.Collections.Generic;
using System.IO;
using Genbox.SimpleS3.Abstracts;
using Genbox.SimpleS3.Abstracts.Marshal;
using Genbox.SimpleS3.Core.Network.Requests.Buckets;
using Genbox.SimpleS3.Core.Network.Responses.Buckets;
using JetBrains.Annotations;

namespace Genbox.SimpleS3.Core.Internal.Marshal.Response.Bucket
{
    [UsedImplicitly]
    internal class DeleteBucketResponseMarshal : IResponseMarshal<DeleteBucketRequest, DeleteBucketResponse>
    {
        public void MarshalResponse(IS3Config config, DeleteBucketRequest request, DeleteBucketResponse response, IDictionary<string, string> headers, Stream responseStream)
        {
        }
    }
}