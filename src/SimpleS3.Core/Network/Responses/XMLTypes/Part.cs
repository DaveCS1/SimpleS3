﻿using System;
using System.Xml.Serialization;
using JetBrains.Annotations;

namespace Genbox.SimpleS3.Core.Network.Responses.XmlTypes
{
    [UsedImplicitly]
    [XmlType]
    public sealed class Part
    {
        public int PartNumber { get; set; }
        public DateTime LastModified { get; set; }
        public string ETag { get; set; }
        public long Size { get; set; }
    }
}