// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Localization
{
    /// <summary>
    /// An <see cref="IStringLocalizerFactory"/> that creates instances of <see cref="ResourceManagerStringLocalizer"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ResourceManagerStringLocalizerFactory"/> offers multiple ways to set the relative path of
    /// resources to be used. They are, in order of precedence:
    /// <see cref="ResourceLocationAttribute"/> -> <see cref="LocalizationOptions.ResourcesPath"/> -> the project root.
    /// </remarks>
    public class ResourceManagerStringLocalizerFactory : IStringLocalizerFactory
    {
        private readonly IResourceNamesCache _resourceNamesCache = new ResourceNamesCache();
        private readonly ConcurrentDictionary<string, ResourceManagerStringLocalizer> _localizerCache =
            new ConcurrentDictionary<string, ResourceManagerStringLocalizer>();
        private readonly string _applicationName;
        private readonly string _resourcesRelativePath;

        /// <summary>
        /// Creates a new <see cref="ResourceManagerStringLocalizer"/>.
        /// </summary>
        /// <param name="hostingEnvironment">The <see cref="IHostingEnvironment"/>.</param>
        /// <param name="localizationOptions">The <see cref="IOptions{LocalizationOptions}"/>.</param>
        public ResourceManagerStringLocalizerFactory(
            IHostingEnvironment hostingEnvironment,
            IOptions<LocalizationOptions> localizationOptions)
        {
            if (hostingEnvironment == null)
            {
                throw new ArgumentNullException(nameof(hostingEnvironment));
            }

            if (localizationOptions == null)
            {
                throw new ArgumentNullException(nameof(localizationOptions));
            }

            _applicationName = hostingEnvironment.ApplicationName;
            _resourcesRelativePath = localizationOptions.Value.ResourcesPath ?? string.Empty;
            if (!string.IsNullOrEmpty(_resourcesRelativePath))
            {
                _resourcesRelativePath = _resourcesRelativePath.Replace(Path.AltDirectorySeparatorChar, '.')
                    .Replace(Path.DirectorySeparatorChar, '.') + ".";
            }
        }

        /// <summary>
        /// Gets the resource prefix used to look up the resource.
        /// </summary>
        /// <param name="typeInfo">The type of the resource to be looked up.</param>
        /// <returns>The prefix for resource lookup.</returns>
        protected virtual string GetResourcePrefix(TypeInfo typeInfo)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }

            return GetResourcePrefix(typeInfo, new AssemblyName(typeInfo.Assembly.FullName).Name, _resourcesRelativePath);
        }

        /// <summary>
        /// Gets the resource prefix used to look up the resource.
        /// </summary>
        /// <param name="typeInfo">The type of the resource to be looked up.</param>
        /// <param name="baseNamespace">The base namespace of the application.</param>
        /// <param name="resourcesRelativePath">The folder containing all resources.</param>
        /// <returns>The prefix for resource lookup.</returns>
        /// <remarks>
        /// For the type "Sample.Controllers.Home" if there's a resourceRelativePath return
        /// "Sample.Resourcepath.Controllers.Home" if there isn't one then it would return "Sample.Controllers.Home".
        /// </remarks>
        protected virtual string GetResourcePrefix(TypeInfo typeInfo, string baseNamespace, string resourcesRelativePath)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }

            if (string.IsNullOrEmpty(baseNamespace))
            {
                throw new ArgumentNullException(nameof(baseNamespace));
            }

            return string.IsNullOrEmpty(resourcesRelativePath)
                ? typeInfo.FullName
                : baseNamespace + "." + resourcesRelativePath + TrimPrefix(typeInfo.FullName, baseNamespace + ".");
        }

        /// <summary>
        /// Gets the resource prefix used to look up the resource.
        /// </summary>
        /// <param name="baseResourceName">The name of the resource to be looked up</param>
        /// <param name="baseNamespace">The base namespace of the application.</param>
        /// <returns>The prefix for resource lookup.</returns>
        protected virtual string GetResourcePrefix(string baseResourceName, string baseNamespace)
        {
            if (string.IsNullOrEmpty(baseResourceName))
            {
                throw new ArgumentNullException(nameof(baseResourceName));
            }

            if (string.IsNullOrEmpty(baseNamespace))
            {
                throw new ArgumentNullException(nameof(baseNamespace));
            }

            var assemblyName = new AssemblyName(baseNamespace);
            var assembly = Assembly.Load(assemblyName);
            var resourceLocation = GetResourcePath(assembly);
            var locationPath = baseNamespace + "." + resourceLocation;

            baseResourceName = locationPath + TrimPrefix(baseResourceName, baseNamespace + ".");

            return baseResourceName;
        }

        /// <summary>
        /// Creates a <see cref="ResourceManagerStringLocalizer"/> using the <see cref="Assembly"/> and
        /// <see cref="Type.FullName"/> of the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="resourceSource">The <see cref="Type"/>.</param>
        /// <returns>The <see cref="ResourceManagerStringLocalizer"/>.</returns>
        public IStringLocalizer Create(Type resourceSource)
        {
            if (resourceSource == null)
            {
                throw new ArgumentNullException(nameof(resourceSource));
            }

            var typeInfo = resourceSource.GetTypeInfo();
            var assembly = typeInfo.Assembly;
            var assemblyName = new AssemblyName(assembly.FullName);
            var resourcePath = GetResourcePath(assembly);

            var baseName = GetResourcePrefix(typeInfo, assemblyName.Name, resourcePath);

            return _localizerCache.GetOrAdd(baseName, _ => CreateResourceManagerStringLocalizer(assembly, baseName));
        }

        /// <summary>
        /// Creates a <see cref="ResourceManagerStringLocalizer"/>.
        /// </summary>
        /// <param name="baseName">The base name of the resource to load strings from.</param>
        /// <param name="location">The location to load resources from.</param>
        /// <returns>The <see cref="ResourceManagerStringLocalizer"/>.</returns>
        public IStringLocalizer Create(string baseName, string location)
        {
            if (baseName == null)
            {
                throw new ArgumentNullException(nameof(baseName));
            }

            location = location ?? _applicationName;

            return _localizerCache.GetOrAdd($"B={baseName},L={location}", _ =>
            {
                var assemblyName = new AssemblyName(location);
                var assembly = Assembly.Load(assemblyName);
                baseName = GetResourcePrefix(baseName, location);

                return CreateResourceManagerStringLocalizer(assembly, baseName);
            });
        }

        /// <summary>Creates a <see cref="ResourceManagerStringLocalizer"/> for the given input.</summary>
        /// <param name="assembly">The assembly to create a <see cref="ResourceManagerStringLocalizer"/> for.</param>
        /// <param name="baseName">The base name of the resource to search for.</param>
        /// <returns>A <see cref="ResourceManagerStringLocalizer"/> for the given <paramref name="assembly"/> and <paramref name="baseName"/>.</returns>
        /// <remarks>This method is virtual for testing purposes only.</remarks>
        protected virtual ResourceManagerStringLocalizer CreateResourceManagerStringLocalizer(
            Assembly assembly,
            string baseName)
        {
            return new ResourceManagerStringLocalizer(
                new ResourceManager(baseName, assembly),
                assembly,
                baseName,
                _resourceNamesCache);
        }

        /// <summary>
        /// Gets the resource prefix used to look up the resource.
        /// </summary>
        /// <param name="location">The general location of the resource.</param>
        /// <param name="baseName">The base name of the resource.</param>
        /// <param name="resourceLocation">The location of the resource within <paramref name="location"/>.</param>
        /// <returns>The resource prefix used to look up the resource.</returns>
        protected virtual string GetResourcePrefix(string location, string baseName, string resourceLocation)
        {
            // Re-root the base name if a resources path is set
            return location + "." + resourceLocation + TrimPrefix(baseName, location + ".");
        }

        /// <summary>Gets a <see cref="ResourceLocationAttribute"/> from the provided <see cref="Assembly"/>.</summary>
        /// <param name="assembly">The assembly to get a <see cref="ResourceLocationAttribute"/> from.</param>
        /// <returns>The <see cref="ResourceLocationAttribute"/> associated with the given <see cref="Assembly"/>.</returns>
        /// <remarks>This method is protected and virtual for testing purposes only.</remarks>
        protected virtual ResourceLocationAttribute GetResourceLocationAttribute(Assembly assembly)
        {
            return assembly.GetCustomAttribute<ResourceLocationAttribute>();
        }

        private string GetResourcePath(Assembly assembly)
        {
            var resourceLocationAttribute = GetResourceLocationAttribute(assembly);

            // If we don't have an attribute assume all assemblies use the same resource location.
            var resourceLocation = resourceLocationAttribute == null
                ? _resourcesRelativePath
                : resourceLocationAttribute.ResourceLocation + ".";
            resourceLocation = resourceLocation
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');

            return resourceLocation;
        }

        private static string TrimPrefix(string name, string prefix)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return name.Substring(prefix.Length);
            }

            return name;
        }
    }
}