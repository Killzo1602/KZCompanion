using System;
using System.IO;
using System.Runtime.CompilerServices;
using DroidDeck.Lib;

namespace DroidDeck.Tests
{
    internal static class TestEnvironment
    {
        [ModuleInitializer]
        internal static void IsolateDataDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "droiddeck-tests-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnvVar, dir);
        }
    }
}
