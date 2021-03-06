using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genbox.SimpleS3.Core.Abstracts;
using Genbox.SimpleS3.Core.Abstracts.Authentication;
using Genbox.SimpleS3.Core.Abstracts.Constants;
using Genbox.SimpleS3.Core.Abstracts.Enums;
using Genbox.SimpleS3.Core.Abstracts.Factories;
using Genbox.SimpleS3.Core.Abstracts.Features;
using Genbox.SimpleS3.Core.Abstracts.Wrappers;
using Genbox.SimpleS3.Core.Builders;
using Genbox.SimpleS3.Core.Common;
using Genbox.SimpleS3.Core.Internals.Enums;
using Genbox.SimpleS3.Core.Internals.Errors;
using Genbox.SimpleS3.Core.Internals.Extensions;
using Genbox.SimpleS3.Core.Internals.Helpers;
using Genbox.SimpleS3.Core.Internals.Pools;
using Genbox.SimpleS3.Core.Network.Requests;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Genbox.SimpleS3.Core.Network
{
    /// <summary>Handles common request and response logic before sending to transport drivers.</summary>
    [PublicAPI]
    public class DefaultRequestHandler : IRequestHandler
    {
        private readonly IAuthorizationBuilder _authBuilder;
        private readonly ILogger<DefaultRequestHandler> _logger;
        private readonly IMarshalFactory _marshaller;
        private readonly IPostMapperFactory _postMapper;
        private readonly INetworkDriver _networkDriver;
        private readonly IOptions<S3Config> _options;
        private readonly IList<IRequestStreamWrapper> _requestStreamWrappers;
        private readonly IValidatorFactory _validator;

        public DefaultRequestHandler(IOptions<S3Config> options, IValidatorFactory validator, IMarshalFactory marshaller, IPostMapperFactory postMapper, INetworkDriver networkDriver, HeaderAuthorizationBuilder authBuilder, ILogger<DefaultRequestHandler> logger, IEnumerable<IRequestStreamWrapper>? requestStreamWrappers = null)
        {
            Validator.RequireNotNull(options, nameof(options));
            Validator.RequireNotNull(validator, nameof(validator));
            Validator.RequireNotNull(marshaller, nameof(marshaller));
            Validator.RequireNotNull(networkDriver, nameof(networkDriver));
            Validator.RequireNotNull(authBuilder, nameof(authBuilder));
            Validator.RequireNotNull(logger, nameof(logger));

            validator.ValidateAndThrow(options.Value);

            _validator = validator;
            _options = options;
            _networkDriver = networkDriver;
            _authBuilder = authBuilder;
            _marshaller = marshaller;
            _postMapper = postMapper;
            _logger = logger;

            if (requestStreamWrappers == null)
                _requestStreamWrappers = Array.Empty<IRequestStreamWrapper>();
            else
                _requestStreamWrappers = requestStreamWrappers.ToList();
        }

        public Task<TResp> SendRequestAsync<TReq, TResp>(TReq request, CancellationToken token = default) where TResp : IResponse, new() where TReq : IRequest
        {
            token.ThrowIfCancellationRequested();

            if (request is PreSignedBaseRequest preSigned)
                return SendPreSigned<TResp>(preSigned, token);

            return SendRequest<TReq, TResp>(request, token);
        }

        private Task<TResp> SendPreSigned<TResp>(PreSignedBaseRequest preSigned, CancellationToken token) where TResp : IResponse, new()
        {
            Stream? requestStream = _marshaller.MarshalRequest(preSigned, _options.Value);
            return HandleResponse<PreSignedBaseRequest, TResp>(preSigned, preSigned.Url, requestStream, token);
        }

        private Task<TResp> SendRequest<TReq, TResp>(TReq request, CancellationToken token) where TResp : IResponse, new() where TReq : IRequest
        {
            request.Timestamp = DateTimeOffset.UtcNow;
            request.RequestId = Guid.NewGuid();

            _logger.LogTrace("Handling {RequestType} with request id {RequestId}", typeof(TReq).Name, request.RequestId);

            S3Config config = _options.Value;
            Stream? requestStream = _marshaller.MarshalRequest(request, config);

            _validator.ValidateAndThrow(request);

            StringBuilder sb = StringBuilderPool.Shared.Rent(200);
            RequestHelper.AppendScheme(sb, config);
            int schemeLength = sb.Length;
            RequestHelper.AppendHost(sb, config, request);

            request.SetHeader(HttpHeaders.Host, sb.ToString(schemeLength, sb.Length - schemeLength));
            request.SetHeader(AmzHeaders.XAmzDate, request.Timestamp, DateTimeFormat.Iso8601DateTime);

            if (requestStream != null)
            {
                foreach (IRequestStreamWrapper wrapper in _requestStreamWrappers)
                {
                    if (wrapper.IsSupported(request))
                        requestStream = wrapper.Wrap(requestStream, request);
                }
            }

            if (!request.Headers.TryGetValue(AmzHeaders.XAmzContentSha256, out string contentHash))
            {
                if (config.PayloadSignatureMode == SignatureMode.Unsigned)
                    contentHash = "UNSIGNED-PAYLOAD";
                else
                    contentHash = requestStream == null ? "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" : CryptoHelper.Sha256Hash(requestStream, true).HexEncode();

                request.SetHeader(AmzHeaders.XAmzContentSha256, contentHash);
            }

            _logger.LogDebug("ContentSha256 is {ContentSha256}", contentHash);

            //We add the authorization header here because we need ALL other headers to be present when we do
            _authBuilder.BuildAuthorization(request);

            RequestHelper.AppendUrl(sb, config, request);
            RequestHelper.AppendQueryParameters(sb, request);
            string url = sb.ToString();
            StringBuilderPool.Shared.Return(sb);

            return HandleResponse<TReq, TResp>(request, url, requestStream, token);
        }

        public async Task<TResp> HandleResponse<TReq, TResp>(TReq request, string url, Stream? requestStream, CancellationToken token) where TResp : IResponse, new() where TReq : IRequest
        {
            _logger.LogDebug("Sending request to {Url}", url);

            (int statusCode, IDictionary<string, string> headers, Stream? responseStream) = await _networkDriver.SendRequestAsync(request.Method, url, request.Headers, requestStream, token).ConfigureAwait(false);

            //Clear sensitive material from the request
            if (request is IContainSensitiveMaterial sensitive)
                sensitive.ClearSensitiveMaterial();

            TResp response = new TResp();
            response.StatusCode = statusCode;
            response.ContentLength = headers.GetHeaderLong(HttpHeaders.ContentLength);
            response.ConnectionClosed = "closed".Equals(headers.GetHeader(HttpHeaders.Connection), StringComparison.OrdinalIgnoreCase);
            response.Date = headers.GetHeaderDate(HttpHeaders.Date, DateTimeFormat.Rfc1123);
            response.Server = headers.GetHeader(HttpHeaders.Server);
            response.ResponseId = headers.GetHeader(AmzHeaders.XAmzId2);
            response.RequestId = headers.GetHeader(AmzHeaders.XAmzRequestId);

            // https://docs.aws.amazon.com/AmazonS3/latest/API/ErrorResponses.html
            response.IsSuccess = !(statusCode == 403 //Forbidden
                                   || statusCode == 400 //BadRequest
                                   || statusCode == 500 //InternalServerError
                                   || statusCode == 416 //RequestedRangeNotSatisfiable
                                   || statusCode == 405 //MethodNotAllowed
                                   || statusCode == 411 //LengthRequired
                                   || statusCode == 404 //NotFound
                                   || statusCode == 501 //NotImplemented
                                   || statusCode == 504 //GatewayTimeout
                                   || statusCode == 301 //MovedPermanently
                                   || statusCode == 412 //PreconditionFailed
                                   || statusCode == 307 //TemporaryRedirect
                                   || statusCode == 409 //Conflict
                                   || statusCode == 503); //ServiceUnavailable

            //Only marshal successful responses
            if (response.IsSuccess)
                _marshaller.MarshalResponse(_options.Value, response, headers, responseStream ?? Stream.Null);
            else if (responseStream != null)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    await responseStream.CopyToAsync(ms, 81920, token).ConfigureAwait(false);

                    if (ms.Length > 0)
                    {
                        ms.Seek(0, SeekOrigin.Begin);

                        using (responseStream)
                            response.Error = ErrorHandler.Create(ms);

                        _logger.LogError("Received error: '{Message}'. Details: '{Details}'", response.Error.Message, response.Error.GetErrorDetails());
                    }
                }
            }

            //We always map even if the request is not successful
            _postMapper.PostMap(_options.Value, request, response);
            return response;
        }
    }
}