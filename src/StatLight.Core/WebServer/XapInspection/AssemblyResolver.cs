﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using StatLight.Core.Common;

namespace StatLight.Core.WebServer.XapInspection
{
    public class AssemblyResolver
    {
        private readonly ILogger _logger;
        private readonly string _originalAssemblyDir;

        private readonly Lazy<string> _silverlightFolder;

        public AssemblyResolver(ILogger logger, DirectoryInfo assemblyDirectoryInfo)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            if (assemblyDirectoryInfo == null) throw new ArgumentNullException("assemblyDirectoryInfo");
            _logger = logger;

            _silverlightFolder = new Lazy<string>(SilverlightFolder);
            _originalAssemblyDir = assemblyDirectoryInfo.FullName;

            _logger.Debug("AssemblyResolver - OriginalAssembly - [{0}]".FormatWith(_originalAssemblyDir));
        }

        public IEnumerable<string> ResolveAllDependentAssemblies(string path)
        {
            _logger.Debug("AssemblyResolver - path: {0}".FormatWith(path));
            Assembly reflectionOnlyLoadFrom = Assembly.ReflectionOnlyLoadFrom(path);
            Debug.Assert(reflectionOnlyLoadFrom != null);
            AssemblyName[] referencedAssemblies = reflectionOnlyLoadFrom.GetReferencedAssemblies();


            var assemblies = new List<string>();

            foreach (var assembly in referencedAssemblies)
            {
                BuildDependentAssemblyList(assembly, assemblies);
            }

            return assemblies;
        }


        private static string SilverlightFolder()
        {
            RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Silverlight");

            if (registryKey == null)
                throw new StatLightException(@"Could not open the registry and find key hklm\SOFTWARE\Microsoft\Silverlight");

            var silverlightVersion = registryKey.GetValue("Version") as string;

            if (silverlightVersion == null)
                throw new StatLightException("Cannot determine the Silverlight version as the registry key lookup returned nothing");

            string programFilesFolder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string silverlightFolder = Path.Combine(programFilesFolder, "Microsoft Silverlight", silverlightVersion);
            if (!Directory.Exists(silverlightFolder))
            {
                throw new DirectoryNotFoundException("Could not find directory " + silverlightFolder);
            }
            return silverlightFolder;
        }

        private string ResolveAssemblyPath(AssemblyName assemblyName)
        {
            var pathsTried = new List<string>();
            Func<string, bool> tryPath = path =>
            {
                if (File.Exists(path))
                    return true;

                pathsTried.Add(path);
                //Log path checked and not found
                return false;
            };


            if (tryPath(assemblyName.CodeBase))
                return assemblyName.CodeBase;

            string newTestPath = Path.Combine(_originalAssemblyDir, assemblyName.Name + ".dll");
            if (tryPath(newTestPath))
                return newTestPath;

            newTestPath = Path.Combine(_silverlightFolder.Value, assemblyName.Name + ".dll");
            if (tryPath(newTestPath))
                return newTestPath;

            // TODO: Look into how to support the following paths...
            // C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\Silverlight\v4.0\System.Windows.dll
            // C:\Program Files (x86)\Microsoft SDKs\Silverlight\v4.0

            throw new FileNotFoundException("Could not find assembly [{0}]. The following paths were searched:{1}{2}{1}Try setting the assembly to 'Copy Local=True' in your project so StatLight can attempt to find the assembly.".FormatWith(assemblyName.FullName,
                                                                                                                                 Environment.NewLine, string.Join(Environment.NewLine, pathsTried.ToArray())));
        }

        private void BuildDependentAssemblyList(AssemblyName assemblyName, List<string> assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException("assemblies");

            var path = ResolveAssemblyPath(assemblyName);

            // Don't load assemblies we've already worked on.
            if (assemblies.Contains(path))
            {
                return;
            }

            Assembly asm = LoadAssembly(path);

            if (asm != null)
            {
                assemblies.Add(path);
                foreach (AssemblyName item in asm.GetReferencedAssemblies())
                {
                    BuildDependentAssemblyList(item, assemblies);
                }
            }

            var temp = new string[assemblies.Count];
            assemblies.CopyTo(temp, 0);
            return;
        }

        private static Assembly LoadAssembly(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            Assembly asm;

            // Look for common path delimiters in the string to see if it is a name or a path.
            if ((path.IndexOf(Path.DirectorySeparatorChar, 0, path.Length) != -1) ||
                (path.IndexOf(Path.AltDirectorySeparatorChar, 0, path.Length) != -1))
            {
                // Load the assembly from a path.
                asm = Assembly.ReflectionOnlyLoadFrom(path);
            }
            else
            {
                // Try as assembly name.
                asm = Assembly.ReflectionOnlyLoad(path);
            }
            return asm;
        }

    }
}