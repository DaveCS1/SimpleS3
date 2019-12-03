﻿using Genbox.SimpleS3.Core.Network.Responses.Properties;

namespace Genbox.SimpleS3.Core.Network.Responses.Objects
{
    public class GetObjectLegalHoldResponse : BaseResponse, IHasRequestCharged
    {
        public bool RequestCharged { get; internal set; }

        public bool LegalHold { get; internal set; }
    }
}