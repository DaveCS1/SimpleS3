﻿using System;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using Genbox.SimpleS3.Core.Abstracts.Authentication;
using Genbox.SimpleS3.Core.Abstracts.Constants;
using Genbox.SimpleS3.Core.Abstracts.Enums;
using Genbox.SimpleS3.Core.Authentication;
using Genbox.SimpleS3.Core.Internals.Extensions;
using Genbox.SimpleS3.Core.Internals.Helpers;
using Genbox.SimpleS3.Core.Network.Requests;
using Genbox.SimpleS3.Core.Network.Requests.Objects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Genbox.SimpleS3.Core.Benchmarks
{
    [MemoryDiagnoser]
    [InProcess]
    public class SignatureBenchmarks
    {
        private readonly ChunkedSignatureBuilder _chunkSigBuilder;
        private readonly DateTimeOffset _date;
        private readonly BaseRequest _req;
        private readonly SignatureBuilder _signatureBuilder;
        private readonly ISigningKeyBuilder _signingKeyBuilder;

        public SignatureBenchmarks()
        {
            S3Config config = new S3Config(new StringAccessKey("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"), AwsRegion.UsEast1);
            config.PayloadSignatureMode = SignatureMode.FullSignature;

            IOptions<S3Config> options = Options.Create(config);

            _signingKeyBuilder = new SigningKeyBuilder(options, NullLogger<SigningKeyBuilder>.Instance);
            IScopeBuilder scopeBuilder = new ScopeBuilder(options);
            _signatureBuilder = new SignatureBuilder(_signingKeyBuilder, scopeBuilder, NullLogger<SignatureBuilder>.Instance, options);
            _chunkSigBuilder = new ChunkedSignatureBuilder(_signingKeyBuilder, scopeBuilder, NullLogger<ChunkedSignatureBuilder>.Instance);

            byte[] data = Encoding.UTF8.GetBytes("Hello world");

            _req = new PutObjectRequest("examplebucket", "benchmark", new MemoryStream(data));
            _req.SetHeader(AmzHeaders.XAmzContentSha256, CryptoHelper.Sha256Hash(data).HexEncode());

            _date = DateTimeOffset.UtcNow;
        }

        [Benchmark]
        public byte[] SignatureKeyBuilder()
        {
            return _signingKeyBuilder.CreateSigningKey(_date, "s3");
        }

        [Benchmark]
        public byte[] SignatureBuilder()
        {
            return _signatureBuilder.CreateSignature(_req);
        }

        [Benchmark]
        public byte[] ChunkedSignatureBuilder()
        {
            return _chunkSigBuilder.CreateChunkSignature(_req, new byte[32], new byte[32], 0, 32);
        }
    }
}