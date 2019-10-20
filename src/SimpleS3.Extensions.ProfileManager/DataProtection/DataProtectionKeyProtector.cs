﻿using Genbox.SimpleS3.Abstracts.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Genbox.SimpleS3.Extensions.ProfileManager.DataProtection
{
    public class DataProtectionKeyProtector : IAccessKeyProtector
    {
        private readonly IDataProtector _protector;

        public DataProtectionKeyProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(nameof(DataProtectionKeyProtector));
        }

        public byte[] ProtectKey(byte[] key)
        {
            return _protector.Protect(key);
        }

        public byte[] UnprotectKey(byte[] key)
        {
            return _protector.Unprotect(key);
        }
    }
}