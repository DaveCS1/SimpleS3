﻿using Genbox.SimpleS3.Extensions.ProfileManager.Abstracts;
using Microsoft.Extensions.DependencyInjection;

namespace Genbox.SimpleS3.Extensions.ProfileManager
{
    public class ProfileManagerBuilder : IProfileManagerBuilder
    {
        public ProfileManagerBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}